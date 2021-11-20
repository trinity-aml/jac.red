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
    public class ApiController : BaseController
    {
        #region torrents
        [Route("api/v1.0/torrents")]
        public ActionResult Index(string search, string type, string sort, string tracker, string voice, string videotype, long relased, long quality, long season, int page = 1, int pageSize = 500)
        {
            #region Выборка 
            IEnumerable<TorrentDetails> query = null;
            var torrents = new List<TorrentDetails>();

            string _s = StringConvert.SearchName(search);
            foreach (var val in tParse.searchDb.Where(i => i.Key.Contains(_s)).Select(i => i.Value.Values))
            {
                foreach (var t in val)
                {
                    if (string.IsNullOrWhiteSpace(type) || t.types.Contains(type))
                        torrents.Add(t);
                }
            }

            if (torrents.Count == 0)
                return Json(torrents);

            #region sort
            switch (sort ?? string.Empty)
            {
                case "sid":
                    query = torrents.OrderByDescending(i => i.sid);
                    break;
                case "pir":
                    query = torrents.OrderByDescending(i => i.pir);
                    break;
                case "size":
                    query = torrents.OrderByDescending(i => i.size);
                    break;
                default:
                    query = torrents.OrderByDescending(i => i.createTime);
                    break;
            }
            #endregion

            if (!string.IsNullOrWhiteSpace(tracker))
                query = query.Where(i => i.trackerName == tracker);

            if (relased > 0)
                query = query.Where(i => i.relased == relased);

            if (quality > 0)
                query = query.Where(i => i.quality == quality);

            if (!string.IsNullOrWhiteSpace(videotype))
                query = query.Where(i => i.videotype == videotype);

            if (!string.IsNullOrWhiteSpace(voice))
                query = query.Where(i => i.voices.Contains(voice));

            if (season > 0)
                query = query.Where(i => i.seasons.Contains((int)season));
            #endregion

            return Json(query.Where(i => i.sid > 0).OrderByDescending(i => i.createTime).Skip((page * pageSize) - pageSize).Take(pageSize).Select(i => new
            {
                tracker = i.trackerName,
                url = i.url != null && i.url.StartsWith("http") ? i.url : null,
                i.title,
                size = (long)(i.size * 1048576),
                i.sizeName,
                i.createTime,
                i.sid,
                i.pir,
                i.magnet,
                i.name,
                i.originalname,
                i.relased,
                i.videotype,
                i.quality,
                i.voices,
                i.seasons,
                i.types
            }));
        }
        #endregion

        #region Jackett
        [Route("api/v2.0/indexers/all/results")]
        public ActionResult Jackett(string query, string title, string title_original, int year, int is_serial, int page = 1, int pageSize = 500)
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
                    string _n = StringConvert.SearchName(title);
                    string _o = StringConvert.SearchName(title_original);

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
                                            if (t.relased == year || t.relased == (year - 1) || t.relased == (year + 1))
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
                                        if (t.relased >= (year - 1))
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
                    Details = i.url != null && i.url.StartsWith("http") ? i.url : null,
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
                }),
                jacred = true
            }), contentType: "application/json; charset=utf-8");
        }
        #endregion
    }
}
