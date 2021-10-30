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
    [Route("cron/rutracker/[action]")]
    public class RutrackerController : BaseController
    {
        static Dictionary<string, List<TaskParse>> taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(IO.File.ReadAllText("Data/temp/rutracker_taskParse.json"));


        #region Parse
        async public Task<string> Parse(int page = 0)
        {
            string log = "";

            foreach (string cat in new List<string>() { "22", "1950", "921", "930", "1457", "313", "312", "312", "119", "1803", "266", "81", "9", "1105", "1389" })
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
                // Наше кино
                "22", "1666", "941",

                // Зарубежное кино
                "1950", "2090", "2221", "2091", "2092", "2093", "2200", "2540", "934", "505",

                // Арт-хаус и авторское кино
                "124",

                // Мультфильмы
                "2343",  "930", "2365", "208", "539", "209",

                // Мультсериалы
                "921", "815", "1460",

                // HD Video
                "1457", "2199", "313", "312", "1247", "2201", "2339", "140",

                // Зарубежные сериалы
                "842", "235", "242", "819", "1531", "721", "1102", "1120", "1214", "489", "387",

                // Русские сериалы
                "9", "81",

                // Корейские и Японские сериалы
                "915", "1939",

                // Зарубежные сериалы (HD Video)
                "119", "1803", "266", "193", "1690", "1459", "825", "1248", "1288",

                // Сериалы Латинской Америки, Турции и Индии 
                "325", "534", "694", "704",

                // Аниме
                "1105", "2491", "1389",

                // Документальные фильмы
                "709",

                // Документалистика
                "46", "671", "2177", "2538", "251", "98", "97", "851", "2178", "821", "2076", "56", "2123", "876", "2139", "1467", "1469", "249", "552", "500", "2112", "1327", "1468", "2168", "2160", "314", "1281", "2110", "979", "2169", "2164", "2166", "2163",

                // Развлекательные телепередачи и шоу, приколы и юмор
                "24", "1959", "939", "1481", "113", "115", "882", "1482", "393", "2537", "532", "827",
            })
            {
                // Получаем html
                string html = await HttpClient.Get($"http://rutracker.net/forum/viewforum.php?f={cat}", timeoutSeconds: 10, useproxy: true);
                if (html == null)
                    continue;

                // Максимальное количиство страниц
                int.TryParse(Regex.Match(html, "Страница <b>1</b> из <b>([0-9]+)</b>").Groups[1].Value, out int maxpages);

                if (maxpages > 0)
                {
                    // Загружаем список страниц в список задач
                    for (int page = 0; page < maxpages; page++)
                    {
                        if (!taskParse.ContainsKey(cat))
                            taskParse.Add(cat, new List<TaskParse>());

                        var val = taskParse[cat];
                        if (val.Find(i => i.page == page) == null)
                            val.Add(new TaskParse(page));
                    }
                }
            }

            IO.File.WriteAllText("Data/temp/rutracker_taskParse.json", JsonConvert.SerializeObject(taskParse));
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
                foreach (var task in taskParse)
                {
                    foreach (var val in task.Value)
                    {
                        if (1 >= DateTime.Now.Hour)
                            break;

                        if (DateTime.Today == val.updateTime)
                            continue;

                        bool res = await parsePage(task.Key, val.page);
                        if (res)
                            val.updateTime = DateTime.Today;
                    }
                }
            }
            catch { }

            _parseAllTaskWork = false;
            return "ok";
        }
        #endregion


        #region parsePage
        async Task<bool> parsePage(string cat, int page, bool parseMagnet = false)
        {
            string html = await HttpClient.Get("http://rutracker.net" + $"/forum/viewforum.php?f={cat}{(page == 0 ? "" : $"&start={page * 50}")}", useproxy: true);
            if (html == null || !html.Contains("RuTracker.org</title>"))
                return false;

            foreach (string row in tParse.ReplaceBadNames(html).Split("class=\"torTopic\"").Skip(1))
            {
                if (string.IsNullOrWhiteSpace(row))
                    continue;

                #region Локальный метод - Match
                string Match(string pattern, int index = 1)
                {
                    string res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim());
                    res = Regex.Replace(res, "[\n\r\t ]+", " ");
                    return res.Trim();
                }
                #endregion

                #region Дата создания
                DateTime.TryParse(Match("<p>([0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}:[0-9]{2})</p>"), out DateTime createTime);
                if (createTime == default)
                    continue;
                #endregion

                #region Данные раздачи
                string url = Match("<a id=\"tt-([0-9]+)\"");
                string title = Match("<a id=\"tt-[0-9]+\"[^>]+>([^\n\r]+)</a>");
                title = Regex.Replace(title, "<[^>]+>", "");
                string _sid = Match("<span class=\"seedmed\"[^>]+><b>([0-9]+)</b>");
                string _pir = Match("<span class=\"leechmed\"[^>]+><b>([0-9]+)</b>");
                string sizeName = Match("dl-stub\">([^<]+)</a>").Replace("&nbsp;", " ");

                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(_sid) || string.IsNullOrWhiteSpace(_pir) || string.IsNullOrWhiteSpace(sizeName))
                    continue;

                url = "http://rutracker.net/forum/viewtopic.php?t=" + url;
                #endregion

                #region Парсим раздачи
                int relased = 0;
                string name = null, originalname = null;

                if (cat is "22" or "1666" or "941" or "1950" or "1950" or "2090" or "2221" or "2091" or "2092" or "2093" or "2200" or "2540" or "934" or "505" or "124" or "1457"
                                or "2199" or "313" or "312" or "1247" or "2201" or "2339" or "140" or "2343" or "930" or "2365" or "208" or "539" or "209" or "709")
                {
                    #region Фильмы
                    // Ниже нуля / Bajocero / Below Zero (Йуис Килес / Lluís Quílez) [2021, Испания, боевик, триллер, криминал, WEB-DLRip] MVO (MUZOBOZ) + Original (Spa) + Sub (Rus, Eng)
                    var g = Regex.Match(title, "^([^/\\(\\[]+) / [^/\\(\\[]+ / ([^/\\(\\[]+) \\([^\\)]+\\) \\[([0-9]+), ").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                    {
                        name = g[1].Value;
                        originalname = g[2].Value;

                        if (int.TryParse(g[3].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        // Белый тигр / The White Tiger (Рамин Бахрани / Ramin Bahrani) [2021, Индия, США, драма, криминал, WEB-DLRip] MVO (HDRezka Studio) + Sub (Rus, Eng) + Original Eng
                        g = Regex.Match(title, "^([^/\\(\\[]+) / ([^/\\(\\[]+) \\([^\\)]+\\) \\[([0-9]+), ").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                        {
                            name = g[1].Value;
                            originalname = g[2].Value;

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            // Дневной дозор (Тимур Бекмамбетов) [2006, Россия, боевик, триллер, фэнтези, BDRip-AVC]
                            g = Regex.Match(title, "^([^/\\(\\[]+) \\([^\\)]+\\) \\[([0-9]+), ").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                            {
                                name = g[1].Value;
                                if (int.TryParse(g[2].Value, out int _yer))
                                    relased = _yer;
                            }
                        }
                    }
                    #endregion
                }
                else if (cat is "842" or "235" or "242" or "819" or "1531" or "721" or "1102" or "1120" or "1214" or "489" or "387" or "9" or "81" or "119" or "1803" or "266" or "193" or "1690" or "1459" or "825" or "1248" or "1288"
                                      or "325" or "534" or "694" or "704" or "921" or "815" or "1460")
                {
                    #region Сериалы
                    if (!Regex.IsMatch(title, "(Сезон|Серии)", RegexOptions.IgnoreCase))
                        continue;

                    if (title.Contains("Сезон:"))
                    {
                        // Уравнитель / Великий уравнитель / The Equalizer / Сезон: 1 / Серии: 1-3 из 4 (Лиз Фридлендер, Солван Наим) [2021, США, Боевик, триллер, драма, криминал, детектив, WEB-DLRip] MVO (TVShows) + Original
                        var g = Regex.Match(title, "^([^/\\(\\[]+) / [^/\\(\\[]+ / ([^/\\(\\[]+) / Сезон: [^/]+ / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                        {
                            name = g[1].Value;
                            originalname = g[2].Value;

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            // 911 служба спасения / 9-1-1 / Сезон: 4 / Серии: 1-6 из 9 (Брэдли Букер, Дженнифер Линч, Гвинет Хердер-Пэйтон) [2021, США, Боевик, триллер, драма, WEB-DLRip] MVO (LostFilm) + Original
                            g = Regex.Match(title, "^([^/\\(\\[]+) / ([^/\\(\\[]+) / Сезон: [^/]+ / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                            {
                                name = g[1].Value;
                                originalname = g[2].Value;

                                if (int.TryParse(g[3].Value, out int _yer))
                                    relased = _yer;
                            }
                            else
                            {
                                // Петербургский роман / Сезон: 1 / Серии: 1-8 из 8 (Александр Муратов) [2018, мелодрама, HDTV 1080i]
                                g = Regex.Match(title, "^([^/\\(\\[]+) / Сезон: [^/]+ / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)").Groups;
                                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                                {
                                    name = g[1].Value;
                                    if (int.TryParse(g[2].Value, out int _yer))
                                        relased = _yer;
                                }
                            }
                        }
                    }
                    else
                    {
                        // Уравнитель / Великий уравнитель / The Equalizer / Серии: 1-3 из 4 (Лиз Фридлендер, Солван Наим) [2021, США, Боевик, триллер, драма, криминал, детектив, WEB-DLRip] MVO (TVShows) + Original
                        var g = Regex.Match(title, "^([^/\\(\\[]+) / [^/\\(\\[]+ / ([^/\\(\\[]+) / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                        {
                            name = g[1].Value;
                            originalname = g[2].Value;

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            // 911 служба спасения / 9-1-1 / Серии: 1-6 из 9 (Брэдли Букер, Дженнифер Линч, Гвинет Хердер-Пэйтон) [2021, США, Боевик, триллер, драма, WEB-DLRip] MVO (LostFilm) + Original
                            g = Regex.Match(title, "^([^/\\(\\[]+) / ([^/\\(\\[]+) / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                            {
                                name = g[1].Value;
                                originalname = g[2].Value;

                                if (int.TryParse(g[3].Value, out int _yer))
                                    relased = _yer;
                            }
                            else
                            {
                                // Петербургский роман / Серии: 1-8 из 8 (Александр Муратов) [2018, мелодрама, HDTV 1080i]
                                g = Regex.Match(title, "^([^/\\(\\[]+) / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)").Groups;
                                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                                {
                                    name = g[1].Value;
                                    if (int.TryParse(g[2].Value, out int _yer))
                                        relased = _yer;
                                }
                            }
                        }
                    }

                    if (Regex.IsMatch(name ?? "", "(Сезон|Серии)", RegexOptions.IgnoreCase) || Regex.IsMatch(originalname ?? "", "(Сезон|Серии)", RegexOptions.IgnoreCase))
                        continue;
                    #endregion
                }
                else if (cat is "1105" or "2491" or "1389" or "915" or "1939" or "46" or "671" or "2177" or "2538" or "251" or "98" or "97" or "851" or "2178" or "821" or "2076" or "56" or "2123" or "876" or "2139" or "1467" 
                                       or "1469" or "249" or "552" or "500" or "2112" or "1327" or "1468" or "2168" or "2160" or "314" or "1281" or "2110" or "979" or "2169" or "2164" or "2166" or "2163"
                                       or "24" or "1959" or "939" or "1481" or "113" or "115" or "882" or "1482" or "393" or "2537" or "532" or "827")
                {
                    #region Нестандартные титлы
                    name = Regex.Match(title, "^([^/\\(\\[]+) ").Groups[1].Value;

                    if (int.TryParse(Regex.Match(title, " \\[([0-9]{4})(,|-) ").Groups[1].Value, out int _yer))
                        relased = _yer;

                    if (Regex.IsMatch(name ?? "", "(Сезон|Серии)", RegexOptions.IgnoreCase))
                        continue;
                    #endregion
                }
                #endregion

                if (!string.IsNullOrWhiteSpace(name))
                {
                    // Id новости
                    string id = Match("<a id=\"tt-([0-9]+)\"");
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
                            var fullNews = await HttpClient.Get(url, useproxy: true);
                            if (fullNews != null)
                                magnet = Regex.Match(fullNews, "href=\"(magnet:[^\"]+)\" class=\"magnet-link\"").Groups[1].Value;
                        }
                    }
                    #endregion

                    #region types
                    string[] types = null;
                    switch (cat)
                    {
                        case "22":
                        case "1666":
                        case "941":
                        case "1950":
                        case "2090":
                        case "2221":
                        case "2091":
                        case "2092":
                        case "2093":
                        case "2200":
                        case "2540":
                        case "934":
                        case "505":
                        case "124":
                        case "1457":
                        case "2199":
                        case "313":
                        case "312":
                        case "1247":
                        case "2201":
                        case "2339":
                        case "140":
                            types = new string[] { "movie" };
                            break;
                        case "2343":
                        case "930":
                        case "2365":
                        case "208":
                        case "539":
                        case "209":
                            types = new string[] { "multfilm" };
                            break;
                        case "921":
                        case "815":
                        case "1460":
                            types = new string[] { "multserial" };
                            break;
                        case "842":
                        case "235":
                        case "242":
                        case "819":
                        case "1531":
                        case "721":
                        case "1102":
                        case "1120":
                        case "1214":
                        case "489":
                        case "387":
                        case "9":
                        case "81":
                        case "119":
                        case "1803":
                        case "266":
                        case "193":
                        case "1690":
                        case "1459":
                        case "825":
                        case "1248":
                        case "1288":
                        case "325":
                        case "534":
                        case "694":
                        case "704":
                        case "915":
                        case "1939":
                            types = new string[] { "serial" };
                            break;
                        case "1105":
                        case "2491":
                        case "1389":
                            types = new string[] { "anime" };
                            break;
                        case "709":
                            types = new string[] { "documovie" };
                            break;
                        case "46":
                        case "671":
                        case "2177":
                        case "2538":
                        case "251":
                        case "98":
                        case "97":
                        case "851":
                        case "2178":
                        case "821":
                        case "2076":
                        case "56":
                        case "2123":
                        case "876":
                        case "2139":
                        case "1467":
                        case "1469":
                        case "249":
                        case "552":
                        case "500":
                        case "2112":
                        case "1327":
                        case "1468":
                        case "2168":
                        case "2160":
                        case "314":
                        case "1281":
                        case "2110":
                        case "979":
                        case "2169":
                        case "2164":
                        case "2166":
                        case "2163":
                            types = new string[] { "docuserial", "documovie" };
                            break;
                        case "24":
                        case "1959":
                        case "939":
                        case "1481":
                        case "113":
                        case "115":
                        case "882":
                        case "1482":
                        case "393":
                        case "2537":
                        case "532":
                        case "827":
                            types = new string[] { "tvshow" };
                            break;
                    }
                    #endregion

                    int.TryParse(_sid, out int sid);
                    int.TryParse(_pir, out int pir);

                    tParse.AddOrUpdate(new TorrentDetails()
                    {
                        trackerName = "rutracker",
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
                foreach (var torrent in tParse.db.Where(i => i.Value.trackerName == "rutracker" && string.IsNullOrWhiteSpace(i.Value.magnet)))
                {
                    var fullNews = await HttpClient.Get(torrent.Key, useproxy: true);
                    if (fullNews != null)
                    {
                        string magnet = Regex.Match(fullNews, "href=\"(magnet:[^\"]+)\" class=\"magnet-link\"").Groups[1].Value;
                        if (!string.IsNullOrWhiteSpace(magnet))
                            torrent.Value.magnet = magnet;
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
