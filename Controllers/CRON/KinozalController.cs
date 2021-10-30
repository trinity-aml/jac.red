using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using JacRed.Engine.CORE;
using JacRed.Engine.Parse;
using JacRed.Models.tParse;
using IO = System.IO;
using JacRed.Engine;

namespace JacRed.Controllers.CRON
{
    [Route("cron/kinozal/[action]")]
    public class KinozalController : BaseController
    {
        static Dictionary<string, Dictionary<string, List<TaskParse>>> taskParse = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, List<TaskParse>>>>(IO.File.ReadAllText("Data/temp/kinozal_taskParse.json"));


        #region Parse
        async public Task<string> Parse(int page)
        {
            string log = "";

            foreach (string cat in new List<string>() 
            {
                // Сериалы
                "45", "46", 

                // Фильмы
                "8", "6", "15", "17", "35", "39", "13", "14", "24", "11", "9", "47", "18", "37", "12",

                // ТВ-шоу
                "49", "50",

                // Мульты
                "21", "22"
            })
            {
                int countreset = 0;
                reset: bool res = await parsePage(cat, page, parseMagnet: true);
                if (!res)
                {
                    if (countreset > 5)
                        continue;

                    await Task.Delay(2000);
                    countreset++;
                    goto reset;
                }

                log += $"{cat} - {page}\n";
            }

            return string.IsNullOrWhiteSpace(log) ? "ok" : log;
        }
        #endregion

        #region UpdateTasksParse
        async public Task<string> UpdateTasksParse()
        {
            foreach (string cat in new List<string>()
            {
                // Сериалы
                "45", "46", 

                // Фильмы
                "8", "6", "15", "17", "35", "39", "13", "14", "24", "11", "9", "47", "18", "37", "12",

                // ТВ-шоу
                "49", "50",

                // Мульты
                "21", "22"
            })
            {
                for (int year = DateTime.Today.Year; year >= 1990; year--)
                {
                    // Получаем html
                    string html = await HttpClient.Get($"http://kinozal.tv/browse.php?c={cat}&d={year}&t=1", timeoutSeconds: 10, useproxy: true);
                    if (html == null)
                        continue;

                    // Максимальное количиство страниц
                    int.TryParse(Regex.Match(html, ">([0-9]+)</a></li><li><a rel=\"next\"").Groups[1].Value, out int maxpages);

                    if (maxpages > 0)
                    {
                        // Загружаем список страниц в список задач
                        for (int page = 0; page <= maxpages; page++)
                        {
                            if (!taskParse.ContainsKey(cat))
                                taskParse.Add(cat, new Dictionary<string, List<TaskParse>>());

                            string arg = $"&d={year}&t=1";
                            var catVal = taskParse[cat];
                            if (!catVal.ContainsKey(arg))
                                catVal.Add(arg, new List<TaskParse>());

                            var val = catVal[arg];
                            if (val.Find(i => i.page == page) == null)
                                val.Add(new TaskParse(page));
                        }
                    }
                }
            }

            IO.File.WriteAllText("Data/temp/kinozal_taskParse.json", JsonConvert.SerializeObject(taskParse));
            return "ok";
        }
        #endregion

        #region ParseAllTask
        static bool _parseAllTaskWork = false;

        async public Task<string> ParseAllTask()
        {
            if (_parseAllTaskWork)
                return "work";

            _parseAllTaskWork = true;

            try
            {
                foreach (var cat in taskParse)
                {
                    foreach (var arg in cat.Value)
                    {
                        foreach (var val in arg.Value)
                        {
                            if (1 >= DateTime.Now.Hour)
                                break;

                            if (DateTime.Today == val.updateTime)
                                continue;

                            bool res = await parsePage(cat.Key, val.page, arg.Key);
                            if (res)
                                val.updateTime = DateTime.Today;
                        }
                    }
                }
            }
            catch { }

            _parseAllTaskWork = false;
            return "ok";
        }
        #endregion


        #region parsePage
        async Task<bool> parsePage(string cat, int page, string arg = null, bool parseMagnet = false)
        {
            string html = await HttpClient.Get($"http://kinozal.tv/browse.php?c={cat}&page={page}" + arg, useproxy: true);
            if (html == null || !html.Contains("Кинозал.ТВ</title>"))
                return false;

            foreach (string row in Regex.Split(tParse.ReplaceBadNames(html), "<tr class=('first bg'|bg)>").Skip(1))
            {
                #region Локальный метод - Match
                string Match(string pattern, int index = 1)
                {
                    string res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim());
                    res = Regex.Replace(res, "[\n\r\t ]+", " ");
                    return res.Trim();
                }
                #endregion

                if (string.IsNullOrWhiteSpace(row))
                    continue;

                #region Дата создания
                DateTime createTime = default;

                if (row.Contains("<td class='s'>сегодня"))
                {
                    createTime = DateTime.Today;
                }
                else if (row.Contains("<td class='s'>вчера"))
                {
                    createTime = DateTime.Today.AddDays(-1);
                }
                else
                {
                    createTime = tParse.ParseCreateTime(Match("<td class='s'>([0-9]{2}.[0-9]{2}.[0-9]{4}) в [0-9]{2}:[0-9]{2}</td>"), "dd.MM.yyyy");
                }

                if (createTime == default)
                    continue;
                #endregion

                #region Данные раздачи
                string url = Match("href=\"/(details.php\\?id=[0-9]+)\"");
                string title = Match("class=\"r[0-9]+\">([^<]+)</a>");
                string _sid = Match("<td class='sl_s'>([0-9]+)</td>");
                string _pir = Match("<td class='sl_p'>([0-9]+)</td>");
                string sizeName = Match("<td class='s'>([0-9\\.,]+ (МБ|ГБ))</td>");

                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(_sid) || string.IsNullOrWhiteSpace(_pir) || string.IsNullOrWhiteSpace(sizeName))
                    continue;

                url = "http://kinozal.tv/" + url;
                #endregion

                #region Парсим раздачи
                int relased = 0;
                string name = null, originalname = null;

                if (cat is "8" or "6" or "15" or "17" or "35" or "39" or "13" or "14" or "24" or "11" or "9" or "47" or "18" or "37" or "12")
                {
                    #region Фильмы
                    // Бэд трип (Приколисты в дороге) / Bad Trip / 2020 / ДБ, СТ / WEB-DLRip (AVC)
                    // Успеть всё за месяц / 30 jours max / 2020 / ЛМ / WEB-DLRip
                    var g = Regex.Match(title, "^([^\\(/]+) (\\([^\\)/]+\\) )?/ ([^\\(/]+) / ([0-9]{4})").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[3].Value) && !string.IsNullOrWhiteSpace(g[4].Value))
                    {
                        name = g[1].Value;
                        originalname = g[3].Value;

                        if (int.TryParse(g[4].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        // Голая правда / 2020 / ЛМ / WEB-DLRip
                        g = Regex.Match(title, "^([^/\\(]+) / ([0-9]{4})").Groups;

                        name = g[1].Value;
                        if (int.TryParse(g[2].Value, out int _yer))
                            relased = _yer;
                    }
                    #endregion
                }
                else if (cat == "45" || cat == "22")
                {
                    #region Сериал - Русский
                    if (row.Contains("сезон"))
                    {
                        // Сельский детектив (6 сезон: 1-2 серии из 2) ([^/]+)?/ 2020 / РУ / WEB-DLRip (AVC)
                        // Любовь в рабочие недели (1 сезон: 1 серия из 15) / 2020 / РУ / WEB-DLRip (AVC)
                        // Фитнес (Королева фитнеса) (1-4 сезон: 1-80 серии из 80) / 2018-2020 / РУ / WEB-DLRip
                        // Бывшие (1-3 сезон: 1-24 серии из 24) / 2016-2020 / РУ / WEB-DLRip (AVC)
                        var g = Regex.Match(title, "^([^\\(/]+) (\\([^\\)/]+\\) )?\\([0-9\\-]+ сезоны?: [^\\)/]+\\) ([^/]+ )?/ ([0-9]{4})").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[4].Value))
                        {
                            name = g[1].Value;

                            if (int.TryParse(g[4].Value, out int _yer))
                                relased = _yer;
                        }
                    }
                    else
                    {
                        // Авантюра на двоих (1-8 серии из 8) / 2021 / РУ /  WEBRip (AVC)
                        // Жизнь после жизни (Небеса подождут) (1-16 серии из 16) / 2016 / РУ / WEB-DLRip
                        var g = Regex.Match(title, "^([^\\(/]+) (\\([^\\)/]+\\) )?\\([^\\)/]+\\) ([^/]+ )?/ ([0-9]{4})").Groups;

                        name = g[1].Value;
                        if (int.TryParse(g[4].Value, out int _yer))
                            relased = _yer;
                    }
                    #endregion
                }
                else if (cat == "46" || cat == "21")
                {
                    #region Сериал - Буржуйский
                    if (row.Contains("сезон"))
                    {
                        // Сокол и Зимний солдат (1 сезон: 1-2 серия из 6) / The Falcon and the Winter Soldier / 2021 / ЛД (#NW), СТ / WEB-DL (1080p)
                        // Голубая кровь (Семейная традиция) (11 сезон: 1-9 серия из 20) / Blue Bloods / 2020 / ПМ (BaibaKo) / WEBRip
                        var g = Regex.Match(title, "^([^\\(/]+) (\\([^\\)/]+\\) )?\\([0-9\\-]+ сезоны?: [^\\)/]+\\) ([^/]+ )?/ ([^\\(/]+) / ([0-9]{4})").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[4].Value) && !string.IsNullOrWhiteSpace(g[5].Value))
                        {
                            name = g[1].Value;
                            originalname = g[4].Value;

                            if (int.TryParse(g[5].Value, out int _yer))
                                relased = _yer;
                        }
                    }
                    else
                    {
                        // Дикий ангел (151-270 серии из 270) / Muneca Brava / 1998-1999 / ПМ / DVB
                        var g = Regex.Match(title, "^([^\\(/]+) (\\([^\\)/]+\\) )?\\([^\\)/]+\\) ([^/]+ )?/ ([^\\(/]+) / ([0-9]{4})").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[4].Value) && !string.IsNullOrWhiteSpace(g[5].Value))
                        {
                            name = g[1].Value;
                            originalname = g[4].Value;

                            if (int.TryParse(g[5].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            g = Regex.Match(title, "^([^\\(/]+) / ([^\\(/]+) / ([0-9]{4})").Groups;
                            name = g[1].Value;
                            originalname = g[2].Value;

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                    }
                    #endregion
                }
                else if (cat == "49" || cat == "50")
                {
                    #region ТВ-шоу
                    // Топ Гир (30 сезон: 1-2 выпуски из 10) / Top Gear / 2021 / ЛМ (ColdFilm) / WEBRip
                    var g = Regex.Match(title, "^([^\\(/]+) (\\([^\\)/]+\\) )?/ ([^\\(/]+) / ([0-9]{4})").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[3].Value) && !string.IsNullOrWhiteSpace(g[4].Value))
                    {
                        name = g[1].Value;
                        originalname = g[3].Value;

                        if (int.TryParse(g[4].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        // Супермама (3 сезон: 1-12 выпуски из 40) / 2021 / РУ / IPTV (1080p)
                        g = Regex.Match(title, "^([^/\\(]+) (\\([^\\)/]+\\) )?/ ([0-9]{4})").Groups;

                        name = g[1].Value;
                        if (int.TryParse(g[3].Value, out int _yer))
                            relased = _yer;
                    }
                    #endregion
                }
                #endregion

                if (!string.IsNullOrWhiteSpace(name))
                {
                    // Id новости
                    string id = Match("href=\"/details.php\\?id=([0-9]+)\"");
                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    // Удаляем торрент где титл не совпадает (возможно обновлен, добавлена серия и т.д)
                    if (tParse.TryGetValue(url, out TorrentDetails _tcache) && _tcache.title != title)
                        tParse.db.TryRemove(url, out _);

                    #region Получаем Magnet
                    string magnet = null;

                    if (parseMagnet)
                    {
                        if (tParse.db.ContainsKey(url) && _tcache?.magnet != null)
                        {
                            magnet = _tcache.magnet;
                        }
                        else
                        {
                            // Получаем Инфо хеш
                            string srv_details = await HttpClient.Post($"http://kinozal.tv/get_srv_details.php?id={id}&action=2", $"id={id}&action=2", AppInit.kinozalCookie, useproxy: true);
                            if (srv_details != null)
                            {
                                // Инфо хеш
                                string torrentHash = new Regex("<ul><li>Инфо хеш: +([^<]+)</li>").Match(srv_details).Groups[1].Value;
                                if (!string.IsNullOrWhiteSpace(torrentHash))
                                    magnet = $"magnet:?xt=urn:btih:{torrentHash}";
                            }
                        }
                    }
                    #endregion

                    #region types
                    string[] types = null;
                    switch (cat)
                    {
                        case "8":
                        case "6":
                        case "15":
                        case "17":
                        case "35":
                        case "39":
                        case "13":
                        case "14":
                        case "24":
                        case "11":
                        case "9":
                        case "47":
                        case "18":
                        case "37":
                        case "12":
                            types = new string[] { "movie" };
                            break;
                        case "45":
                        case "46":
                            types = new string[] { "serial" };
                            break;
                        case "49":
                        case "50":
                            types = new string[] { "tvshow" };
                            break;
                        case "21":
                        case "22":
                            types = new string[] { "multfilm", "multserial" };
                            break;
                    }
                    #endregion

                    int.TryParse(_sid, out int sid);
                    int.TryParse(_pir, out int pir);

                    tParse.AddOrUpdate(new TorrentDetails()
                    {
                        trackerName = "kinozal",
                        types = types,
                        url = url,
                        title = title,
                        sid = sid,
                        pir = pir,
                        sizeName = sizeName,
                        createTime = createTime,
                        magnet = magnet,
                        name = name,
                        originalname = originalname,
                        relased = relased
                    });
                }
            }

            return true;
        }
        #endregion

        #region parseMagnet
        static bool _parseMagnetWork = false;

        async public Task<string> parseMagnet()
        {
            if (_parseMagnetWork)
                return "work";

            _parseMagnetWork = true;

            try
            {
                foreach (var torrent in tParse.db.Where(i => i.Value.trackerName == "kinozal" && string.IsNullOrWhiteSpace(i.Value.magnet)))
                {
                    string Id = Regex.Match(torrent.Key, "\\?id=([0-9]+)").Groups[1].Value;

                    // Получаем Инфо хеш
                    string srv_details = await HttpClient.Post($"http://kinozal.tv/get_srv_details.php?id={Id}&action=2", $"id={Id}&action=2", "__cfduid=d476ac2d9b5e18f2b67707b47ebd9b8cd1560164391; uid=20520283; pass=ouV5FJdFCd;", useproxy: true);
                    if (srv_details != null)
                    {
                        // Инфо хеш
                        string torrentHash = new Regex("<ul><li>Инфо хеш: +([^<]+)</li>").Match(srv_details).Groups[1].Value;
                        if (!string.IsNullOrWhiteSpace(torrentHash))
                            torrent.Value.magnet = $"magnet:?xt=urn:btih:{torrentHash}";
                    }
                }
            }
            catch { }

            _parseMagnetWork = false;
            return "ok";
        }
        #endregion
    }
}
