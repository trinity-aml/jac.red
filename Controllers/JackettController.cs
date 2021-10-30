using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using JacRed.Engine.Parse;
using JacRed.Models.tParse;
using Newtonsoft.Json;
using JacRed.Engine.CORE;
using System.Text.RegularExpressions;
using JacRed.Engine;

namespace JacRed.Controllers
{
    public class JackettController : BaseController
    {
        [Route("api/v2.0/indexers/all/results")]
        public ActionResult Index(string query, string title, string title_original, int year, int is_serial, int page = 1, int pageSize = 500)
        {
            var torrents = new List<TorrentDetails>();

            #region Запрос с NUM
            bool IsNum = false;
            var mNum = Regex.Match(query, "^([^a-z-A-Z]+) ([^а-я-А-Я]+) ([0-9]{4})$");

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(title_original) &&
                mNum.Success)
            {
                if (Regex.IsMatch(mNum.Groups[2].Value, "[a-zA-Z]{4}"))
                {
                    IsNum = true;
                    var g = mNum.Groups;

                    title = g[1].Value;
                    title_original = g[2].Value;
                    year = int.Parse(g[3].Value);
                }
            }
            #endregion

            if (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(title_original))
            {
                #region Точный поиск
                if (IsNum)
                {
                    #region NUM
                    string _n = StringConvert.SearchName(title);
                    string _o = StringConvert.SearchName(title_original);

                    foreach (var val in tParse.searchDb.Where(i => i.Key == $"{_n}:{_o}").Select(i => i.Value.Values))
                    {
                        foreach (var t in val)
                        {
                            if (t.types.Contains("movie") || t.types.Contains("multfilm") || t.types.Contains("anime") || t.types.Contains("documovie"))
                            {
                                if (t.relased == year)
                                    torrents.Add(t);
                            }
                            else
                            {
                                if (t.relased >= year)
                                    torrents.Add(t);
                            }
                        }
                    }
                    #endregion
                }
                else
                {
                    #region LAMPA
                    string _n = StringConvert.SearchName(title);
                    string _o = StringConvert.SearchName(title_original);
                    bool exactName = _n != _o && !string.IsNullOrWhiteSpace(_n) && Regex.IsMatch(_n, "[а-яА-Я]{4}") && !string.IsNullOrWhiteSpace(_o) && Regex.IsMatch(_o, "[a-zA-Z]{4}");

                    // Быстрая выборка по совпадению ключа в имени
                    foreach (var val in tParse.searchDb.Where(i => (_n != null && i.Key.Contains(_n)) || (_o != null && i.Key.Contains(_o))).Select(i => i.Value.Values))
                    {
                        foreach (var t in val)
                        {
                            string name = StringConvert.SearchName(t.name);
                            string originalname = StringConvert.SearchName(t.originalname);

                            // Точная выборка по name или originalname
                            if ((_n != null && _n == name) || (_o != null && _o == originalname))
                            {
                                #region exactName - name и originalname должны совпадать
                                if (exactName && t.trackerName != "toloka")
                                {
                                    if (name != _n || originalname != _o)
                                        continue;
                                }
                                #endregion

                                if (is_serial == 2)
                                {
                                    #region Сериал
                                    if (t.types.Contains("serial") || t.types.Contains("multserial") || t.types.Contains("anime") || t.types.Contains("docuserial") || t.types.Contains("tvshow"))
                                    {
                                        if (year > 0)
                                        {
                                            if (t.relased >= year)
                                                torrents.Add(t);
                                        }
                                        else
                                        {
                                            torrents.Add(t);
                                        }
                                    }
                                    #endregion
                                }
                                else if (is_serial == 1)
                                {
                                    #region Фильм
                                    if (t.types.Contains("movie") || t.types.Contains("multfilm") || t.types.Contains("anime") || t.types.Contains("documovie"))
                                    {
                                        if (year > 0)
                                        {
                                            if (t.relased == year || /*t.relased == (year - 1) ||*/ t.relased == (year + 1))
                                                torrents.Add(t);
                                        }
                                        else
                                        {
                                            torrents.Add(t);
                                        }
                                    }
                                    #endregion
                                }
                                else
                                {
                                    #region Неизвестно
                                    if (year > 0)
                                    {
                                        if (t.relased >= year)
                                            torrents.Add(t);
                                    }
                                    else
                                    {
                                        torrents.Add(t);
                                    }
                                    #endregion
                                }
                            }
                        }
                    }
                    #endregion
                }
                #endregion
            }
            else if (!string.IsNullOrWhiteSpace(query))
            {
                #region Обычный поиск
                string _s = StringConvert.SearchName(query);

                foreach (var val in tParse.searchDb.Where(i => i.Key.Contains(_s)).Select(i => i.Value.Values))
                {
                    foreach (var t in val)
                        torrents.Add(t);
                }
                #endregion
            }

            return Content(JsonConvert.SerializeObject(new
            {
                Results = torrents.Where(i => i.sid > 0).OrderByDescending(i => i.createTime).Skip((page * pageSize) - pageSize).Take(pageSize).Select(i => new 
                {
                    Tracker = i.trackerName,
                    Title = i.title,
                    Size = (long)(i.size * 1048576),
                    PublishDate = i.createTime,
                    Seeders = i.sid,
                    Peers = i.pir,
                    MagnetUri = i.magnet,
                    i.name,
                    i.originalname,
                    i.relased,
                    i.videotype,
                    i.quality,
                    i.voices,
                    i.seasons,
                    i.types
                })
            }), contentType: "application/json; charset=utf-8");
        }
    }
}
