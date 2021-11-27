using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using JacRed.Engine.Parse;
using JacRed.Models.tParse;
using Newtonsoft.Json;
using JacRed.Engine.CORE;
using System.Text.RegularExpressions;
using JacRed.Engine;
using JacRed.Models.TorrServer;

namespace JacRed.Controllers
{
    public class ApiController : BaseController
    {
        #region Jackett
        [Route("api/v2.0/indexers/all/results")]
        public ActionResult Jackett(string query, string title, string title_original, int year, int is_serial, Dictionary<string, string> category)
        {
            var torrents = new List<TorrentDetails>();

            #region Запрос с NUM
            var mNum = Regex.Match(query ?? string.Empty, "^([^a-z-A-Z]+) ([^а-я-А-Я]+) ([0-9]{4})$");

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(title_original) &&
                mNum.Success)
            {
                if (Regex.IsMatch(mNum.Groups[2].Value, "[a-zA-Z]{4}"))
                {
                    var g = mNum.Groups;

                    title = g[1].Value;
                    title_original = g[2].Value;
                    year = int.Parse(g[3].Value);
                }
            }
            #endregion

            #region category
            if (is_serial == 0 && category != null)
            {
                string cat = category.FirstOrDefault().Value;
                if (cat != null)
                {
                    if (cat == "5020" || cat == "2010")
                        is_serial = 3; // tvshow
                    else if (cat == "5080")
                        is_serial = 4; // док
                    else if (cat == "5070")
                        is_serial = 5; // аниме
                    else if (cat.StartsWith("20"))
                        is_serial = 1;
                    else if (cat.StartsWith("50"))
                        is_serial = 2;
                }
            }
            #endregion

            if (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(title_original))
            {
                #region Точный поиск
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
                            if (is_serial == 1)
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
                            else if (is_serial == 2)
                            {
                                #region Сериал
                                if (t.types.Contains("serial") || t.types.Contains("multserial") || t.types.Contains("anime") || t.types.Contains("docuserial") || t.types.Contains("tvshow"))
                                {
                                    if (year > 0)
                                    {
                                        if (t.relased >= (year - 1))
                                            torrents.Add(t);
                                    }
                                    else
                                    {
                                        torrents.Add(t);
                                    }
                                }
                                #endregion
                            }
                            else if (is_serial == 3)
                            {
                                #region tvshow
                                if (t.types.Contains("tvshow"))
                                {
                                    if (year > 0)
                                    {
                                        if (t.relased >= (year - 1))
                                            torrents.Add(t);
                                    }
                                    else
                                    {
                                        torrents.Add(t);
                                    }
                                }
                                #endregion
                            }
                            else if (is_serial == 4)
                            {
                                #region docuserial / documovie
                                if (t.types.Contains("docuserial") || t.types.Contains("documovie"))
                                {
                                    if (year > 0)
                                    {
                                        if (t.relased >= (year - 1))
                                            torrents.Add(t);
                                    }
                                    else
                                    {
                                        torrents.Add(t);
                                    }
                                }
                                #endregion
                            }
                            else if (is_serial == 5)
                            {
                                #region anime
                                if (t.types.Contains("anime"))
                                {
                                    if (year > 0)
                                    {
                                        if (t.relased >= (year - 1))
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
                                    if (t.types.Contains("movie") || t.types.Contains("multfilm") || t.types.Contains("documovie"))
                                    {
                                        if (t.relased == year || t.relased == (year - 1) || t.relased == (year + 1))
                                            torrents.Add(t);
                                    }
                                    else
                                    {
                                        if (t.relased >= (year - 1))
                                            torrents.Add(t);
                                    }
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
            else if (!string.IsNullOrWhiteSpace(query))
            {
                #region Обычный поиск
                string _s = StringConvert.SearchName(query);

                #region torrentsSearch
                void torrentsSearch(bool exact)
                {
                    foreach (var val in tParse.searchDb.Where(i => i.Key.Contains(_s)).Select(i => i.Value.Values))
                    {
                        foreach (var t in val)
                        {
                            if (exact)
                            {
                                if (StringConvert.SearchName(t.name) != _s && StringConvert.SearchName(t.originalname) != _s)
                                    continue;
                            }

                            if (is_serial == 1)
                            {
                                if (t.types.Contains("movie") || t.types.Contains("multfilm") || t.types.Contains("anime") || t.types.Contains("documovie"))
                                    torrents.Add(t);
                            }
                            else if (is_serial == 2)
                            {
                                if (t.types.Contains("serial") || t.types.Contains("multserial") || t.types.Contains("anime") || t.types.Contains("docuserial") || t.types.Contains("tvshow"))
                                    torrents.Add(t);
                            }
                            else if (is_serial == 3)
                            {
                                if (t.types.Contains("tvshow"))
                                    torrents.Add(t);
                            }
                            else if (is_serial == 4)
                            {
                                if (t.types.Contains("docuserial") || t.types.Contains("documovie"))
                                    torrents.Add(t);
                            }
                            else if (is_serial == 5)
                            {
                                if (t.types.Contains("anime"))
                                    torrents.Add(t);
                            }
                            else
                            {
                                torrents.Add(t);
                            }
                        }
                    }
                }
                #endregion

                torrentsSearch(exact: true);
                if (torrents.Count == 0)
                    torrentsSearch(exact: false);
                #endregion
            }

            #region getCategoryIds
            HashSet<int> getCategoryIds(TorrentDetails t, out string categoryDesc)
            {
                categoryDesc = null;
                HashSet<int> categoryIds = new HashSet<int>();

                foreach (string type in t.types)
                {
                    switch (type)
                    {
                        case "movie":
                            categoryDesc = "Movies";
                            categoryIds.Add(2000);
                            break;

                        case "serial":
                            categoryDesc = "TV";
                            categoryIds.Add(5000);
                            break;

                        case "documovie":
                        case "docuserial":
                            categoryDesc = "TV/Documentary";
                            categoryIds.Add(5080);
                            break;

                        case "tvshow":
                            categoryDesc = "TV/Foreign";
                            categoryIds.Add(5020);
                            categoryIds.Add(2010);
                            break;

                        case "anime":
                            categoryDesc = "TV/Anime";
                            categoryIds.Add(5070);
                            break;
                    }
                }

                return categoryIds;
            }
            #endregion

            return Content(JsonConvert.SerializeObject(new
            {
                Results = torrents.Where(i => i.sid > 0 || i.trackerName == "toloka").OrderByDescending(i => i.createTime).Take(2_000).Select(i => new
                {
                    Tracker = i.trackerName,
                    Details = i.url != null && i.url.StartsWith("http") ? i.url : null,
                    Title = i.title,
                    Size = (long)(i.size * 1048576),
                    PublishDate = i.createTime,
                    Category = getCategoryIds(i, out string categoryDesc),
                    CategoryDesc = categoryDesc,
                    Seeders = i.sid,
                    Peers = i.pir,
                    MagnetUri = i.magnet
                })
            }), contentType: "application/json; charset=utf-8");
        }
        #endregion

        #region Torrents
        [Route("api/v1.0/torrents")]
        public JsonResult Torrents(string search, string altname, bool exact, string type, string sort, string tracker, string voice, string videotype, long relased, long quality, long season)
        {
            #region Выборка 
            IEnumerable<TorrentDetails> query = null;
            var torrents = new List<TorrentDetails>();

            if (string.IsNullOrWhiteSpace(search))
                return Json(torrents);

            string _s = StringConvert.SearchName(search);
            string _altsearch = StringConvert.SearchName(altname);

            if (exact)
            {
                #region Точный поиск
                foreach (var val in tParse.searchDb.Where(i => i.Key.Contains(_s) || (_altsearch != null && i.Key.Contains(_altsearch))).Select(i => i.Value.Values))
                {
                    foreach (var t in val)
                    {
                        if (string.IsNullOrWhiteSpace(type) || t.types.Contains(type))
                        {
                            string _n = StringConvert.SearchName(t.name);
                            string _o = StringConvert.SearchName(t.originalname);

                            if (_n == _s || _o == _s || (_altsearch != null && (_n == _altsearch || _o == _altsearch)))
                                torrents.Add(t);
                        }
                    }
                }
                #endregion
            }
            else
            {
                #region Поиск по совпадению ключа в имени
                foreach (var val in tParse.searchDb.Where(i => i.Key.Contains(_s) || (_altsearch != null && i.Key.Contains(_altsearch))).Select(i => i.Value.Values))
                {
                    foreach (var t in val)
                    {
                        if (string.IsNullOrWhiteSpace(type) || t.types.Contains(type))
                            torrents.Add(t);
                    }
                }
                #endregion
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

            #region getMediaInfo
            List<MediaInfo> getMediaInfo(string magnet)
            {
                if (magnet != null)
                {
                    #region Обновляем magnet
                    if (magnet.Contains("magnet:?xt=urn"))
                        magnet = Regex.Match(magnet, "urn:btih:([a-zA-Z0-9]+)").Groups[1].Value;

                    magnet = magnet.ToLower();
                    #endregion

                    // Отдаем кеш
                    if (TorrServerAPI.db.TryGetValue(magnet, out List<MediaInfo> _cache))
                        return _cache;
                }

                return null;
            }
            #endregion

            return Json(query.Where(i => i.sid > 0 || i.trackerName == "toloka").Take(5_000).Select(i => new
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
                i.types,
                media = getMediaInfo(i.magnet)?.Select(m => new
                {
                    tid = m.Id,
                    path = m.Path,
                    size = m.FileSize,
                })
            }));
        }
        #endregion
    }
}
