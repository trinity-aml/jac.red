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
    [Route("cron/bitru/[action]")]
    public class BitruController : BaseController
    {
        static Dictionary<string, List<TaskParse>> taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(IO.File.ReadAllText("Data/temp/bitru_taskParse.json"));


        #region Parse
        async public Task<string> Parse(int page = 1)
        {
            string log = "";

            // movie     - Фильмы    | Фильмы
            // serial    - Сериалы   | Сериалы
            foreach (string cat in new List<string>() { "movie", "serial" })
            {
                int countreset = 0;
                reset: bool res = await parsePage(cat, page, parseMagnet: true);
                if (!res)
                {
                    if (countreset > 2)
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
            // movie     - Фильмы    | Фильмы
            // serial    - Сериалы   | Сериалы
            foreach (string cat in new List<string>() { "movie", "serial" })
            {
                // Получаем html
                string html = await HttpClient.Get($"https://bitru.org/browse.php?tmp={cat}", timeoutSeconds: 10, useproxy: true);
                if (html == null)
                    continue;

                // Максимальное количиство страниц
                int.TryParse(Regex.Match(html, $"<a href=\"browse.php\\?tmp={cat}&page=[^\"]+\">([0-9]+)</a></div>").Groups[1].Value, out int maxpages);

                if (maxpages > 0)
                {
                    // Загружаем список страниц в список задач
                    for (int page = 1; page <= maxpages; page++)
                    {
                        if (!taskParse.ContainsKey(cat))
                            taskParse.Add(cat, new List<TaskParse>());

                        var val = taskParse[cat];
                        if (val.Find(i => i.page == page) == null)
                            val.Add(new TaskParse(page));
                    }
                }
            }

            IO.File.WriteAllText("Data/temp/bitru_taskParse.json", JsonConvert.SerializeObject(taskParse));
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
            string html = await HttpClient.Get($"https://bitru.org/browse.php?tmp={cat}&page={page}", useproxy: true);
            if (html == null || !html.Contains(" - bitru.org</title>"))
                return false;

            foreach (string row in tParse.ReplaceBadNames(html).Split("<div class=\"b-title\"").Skip(1))
            {
                if (row.Contains(">Аниме</a>") || row.Contains(">Мульт"))
                    continue;

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

                if (row.Contains("<span>Сегодня"))
                {
                    createTime = DateTime.Today;
                }
                else if (row.Contains("<span>Вчера"))
                {
                    createTime = DateTime.Today.AddDays(-1);
                }
                else
                {
                    createTime = tParse.ParseCreateTime(Match("<div class=\"ellips\"><span>([0-9]{2} [^ ]+ [0-9]{4}) в [0-9]{2}:[0-9]{2} от <a"), "dd.MM.yyyy");
                }

                if (createTime == default)
                    continue;
                #endregion

                #region Данные раздачи
                string url = Match("href=\"(details.php\\?id=[0-9]+)\"");
                string title = Match("<div class=\"it-title\">([^<]+)</div>");
                string _sid = Match("<span class=\"b-seeders\">([0-9]+)</span>");
                string _pir = Match("<span class=\"b-leechers\">([0-9]+)</span>");
                string sizeName = Match("title=\"Размер\">([^<]+)</td>");

                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(_sid) || string.IsNullOrWhiteSpace(_pir) || string.IsNullOrWhiteSpace(sizeName))
                    continue;

                url = "http://bitru.org/" + url;
                #endregion

                #region Парсим раздачи
                int relased = 0;
                string name = null, originalname = null;

                if (cat == "movie")
                {
                    #region Фильмы
                    // Звонок из прошлого / Звонок / Kol / The Call (2020)
                    var g = Regex.Match(title, "^([^/\\(]+) / [^/]+ / [^/]+ / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                    {
                        name = g[1].Value;
                        originalname = g[2].Value;

                        if (int.TryParse(g[3].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        // Код бессмертия / Код молодости / Eternal Code (2019)
                        g = Regex.Match(title, "^([^/\\(]+) / [^/]+ / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                        {
                            name = g[1].Value;
                            originalname = g[2].Value;

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            // Брешь / Breach (2020)
                            g = Regex.Match(title, "^([^/\\(]+) / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                            {
                                name = g[1].Value;
                                originalname = g[2].Value;

                                if (int.TryParse(g[3].Value, out int _yer))
                                    relased = _yer;
                            }
                            else
                            {
                                // Жертва (2020)
                                g = Regex.Match(title, "^([^/\\(]+) \\(([0-9]{4})\\)").Groups;

                                name = g[1].Value;
                                if (int.TryParse(g[2].Value, out int _yer))
                                    relased = _yer;
                            }
                        }
                    }
                    #endregion
                }
                else if (cat == "serial")
                {
                    #region Сериалы
                    if (row.Contains("сезон"))
                    {
                        // Золотое Божество 3 сезон (1-12 из 12) / Gōruden Kamui / Golden Kamuy (2020)
                        var g = Regex.Match(title, "^([^/\\(]+) [0-9\\-]+ сезон [^/]+ / [^/]+ / ([^/\\(]+) \\(([0-9]{4})(\\)|-)").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                        {
                            name = g[1].Value;
                            originalname = g[2].Value;

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            // Ход королевы / Ферзевый гамбит 1 сезон (1-7 из 7) / The Queen's Gambit (2020)
                            g = Regex.Match(title, "^([^/\\(]+) / [^/]+ / ([^/\\(]+) \\(([0-9]{4})(\\)|-)").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                            {
                                name = g[1].Value;
                                originalname = g[2].Value;

                                if (int.TryParse(g[3].Value, out int _yer))
                                    relased = _yer;
                            }
                            else
                            {
                                // Доллар 1 сезон (1-15 из 15) / Dollar (2019)
                                // Эш против Зловещих мертвецов 1-3 сезон (1-30 из 30) / Ash vs Evil Dead (2015-2018)
                                g = Regex.Match(title, "^([^/\\(]+) [0-9\\-]+ сезон [^/]+ / ([^/\\(]+) \\(([0-9]{4})(\\)|-)").Groups;
                                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                                {
                                    name = g[1].Value;
                                    originalname = g[2].Value;

                                    if (int.TryParse(g[3].Value, out int _yer))
                                        relased = _yer;
                                }
                                else
                                {
                                    // СашаТаня 6 сезон (1-19 из 22) (2021)
                                    // Метод 1-2 сезон (1-26 из 32) (2015-2020)
                                    g = Regex.Match(title, "^([^/\\(]+) [0-9\\-]+ сезон \\([^\\)]+\\) +\\(([0-9]{4})(\\)|-)").Groups;

                                    name = g[1].Value;
                                    if (int.TryParse(g[2].Value, out int _yer))
                                        relased = _yer;
                                }
                            }
                        }
                    }
                    else
                    {
                        // Проспект обороны (1-16 из 16) (2019)
                        var g = Regex.Match(title, "^([^/\\(]+) \\([^\\)]+\\) +\\(([0-9]{4})(\\)|-)").Groups;

                        name = g[1].Value;
                        if (int.TryParse(g[2].Value, out int _yer))
                            relased = _yer;
                    }
                    #endregion
                }
                #endregion

                if (!string.IsNullOrWhiteSpace(name))
                {
                    // Id новости
                    string id = Match("href=\"details.php\\?id=([0-9]+)\"");
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
                            byte[] torrent = await HttpClient.Download($"https://bitru.org/download.php?id={id}", referer: $"https://bitru.org/details.php?id={id}", useproxy: true);
                            magnet = BencodeTo.Magnet(torrent);
                        }
                    }
                    #endregion

                    #region types
                    string[] types = null;
                    switch (cat)
                    {
                        case "movie":
                            types = new string[] { "movie" };
                            break;
                        case "serial":
                            types = new string[] { "serial" };
                            break;
                    }
                    #endregion

                    int.TryParse(_sid, out int sid);
                    int.TryParse(_pir, out int pir);

                    tParse.AddOrUpdate(new TorrentDetails()
                    {
                        trackerName = "bitru",
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
                foreach (var torrent in tParse.db.Where(i => i.Value.trackerName == "bitru" && string.IsNullOrWhiteSpace(i.Value.magnet)))
                {
                    string url = torrent.Key.Replace("http:", "https:");

                    byte[] _t = await HttpClient.Download(url.Replace("/details.php", "/download.php"), referer: url, useproxy: true);
                    string magnet = BencodeTo.Magnet(_t);

                    if (!string.IsNullOrWhiteSpace(magnet))
                        torrent.Value.magnet = magnet;
                }
            }
            catch { }

            _parseMagnetWork = false;
            return "ok";
        }
        #endregion
    }
}
