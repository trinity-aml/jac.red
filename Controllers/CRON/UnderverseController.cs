using System;
using System.Collections.Generic;
using System.Globalization;
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
    [Route("cron/underverse/[action]")]
    public class UnderverseController : BaseController
    {
        static Dictionary<string, List<TaskParse>> taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(IO.File.ReadAllText("Data/temp/underverse_taskParse.json"));


        #region Parse
        async public Task<string> Parse(int page = 0)
        {
            string log = "";

            foreach (string cat in new List<string>() { "99", "100", "1023", "1024", "106", "105", "1019", "1018" })
            {
                int countreset = 0;
                reset: bool res = await parsePage(cat, page);
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
                // Зарубежное кино
                "99", "100",

                // Новинки кино [CAMRip, TS, DVDScr, WP, TC]
                "704",

                // Мультфильмы
                "1023", "1024",

                // Наше кино
                "106", "105",

                // Документальное кино
                "113", "114",

                // Телепередачи и шоу
                "78", "81", "82", 

                // Документальное видео
                "59", "60", "62", "64",

                // Сериалы
                "1019", "1018"
            })
            {
                // Получаем html
                string html = await HttpClient.Get($"https://underver.se/viewforum.php?f={cat}", timeoutSeconds: 10, useproxy: true);
                if (html == null)
                    continue;

                // Максимальное количиство страниц
                int.TryParse(Regex.Match(html, "Страница <span [^>]+>1</span> из <span [^>]+>([0-9]+)</span>").Groups[1].Value, out int maxpages);

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

            IO.File.WriteAllText("Data/temp/underverse_taskParse.json", JsonConvert.SerializeObject(taskParse));
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
        async Task<bool> parsePage(string cat, int page)
        {
            string html = await HttpClient.Get("https://underver.se" + $"/viewforum.php?f={cat}{(page == 0 ? "" : $"&start={page * 50}")}", useproxy: true);
            if (html == null || !html.Contains("underver.se</title>"))
                return false;

            bool allParse = true;

            foreach (string row in tParse.ReplaceBadNames(html).Split("<tr id=\"tr-").Skip(1))
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
                string _createTime = Match("<p>([0-9]{2}\\.[0-9]{2}\\.[0-9]{4} [0-9]{2}:[0-9]{2})</p>");
                if (!DateTime.TryParseExact(_createTime, "dd.MM.yyyy HH:mm", new CultureInfo("ru-RU"), DateTimeStyles.None, out DateTime createTime) || createTime == default)
                    continue;
                #endregion

                #region Данные раздачи
                string url = Match("href=\"[^\"]+/(viewtopic.php\\?t=[0-9]+)\"");
                string title = Match("class=\"torTopic\"><b>([^<]+)</b></a>");
                string _sid = Match("title=\"Seeders\"><b>([0-9]+)</b>");
                string _pir = Match("title=\"Leechers\"><b>([0-9]+)</b>");
                string sizeName = Match("<a href=\"[^\"]+/download.php\\?id=[^\"]+\" [^>]+>([^<]+)</a>").Replace("&nbsp;", " ").Replace(",", ".");

                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(_sid) || string.IsNullOrWhiteSpace(_pir) || string.IsNullOrWhiteSpace(sizeName))
                    continue;

                url = "http://underver.se/" + url;
                #endregion

                #region Парсим раздачи
                int relased = 0;
                string name = null, originalname = null;

                if (cat is "99" or "100" or "1023" or "1024" or "106" or "105")
                {
                    #region Фильмы
                    // Осторожно, Кенгуру! / Хроники кенгуру / Die Kanguru-Chroniken (Дани Леви / Dani Levy) [2020 г., комедия, BDRemux 1080p] Dub (iTunes) + Original (Ger)
                    var g = Regex.Match(title, "^([^/\\(\\[]+) / [^/\\(\\[]+ / ([^/\\(\\[]+) \\([^\\)]+\\) \\[([0-9]{4})").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                    {
                        name = g[1].Value;
                        originalname = g[2].Value;

                        if (int.TryParse(g[3].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        // Звонок. Последняя глава / Sadako (Хидэо Наката / Hideo Nakata) [2019 г., ужасы, BDRemux 1080p] Dub (iTunes) + Original (Jap)
                        g = Regex.Match(title, "^([^/\\(\\[]+) / ([^/\\(\\[]+) \\([^\\)]+\\) \\[([0-9]{4})").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                        {
                            name = g[1].Value;
                            originalname = g[2].Value;

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            // Дневной дозор (Тимур Бекмамбетов) [2006 г., Россия, боевик, триллер, фэнтези, BDRip-AVC]
                            g = Regex.Match(title, "^([^/\\(\\[]+) \\([^\\)]+\\) \\[([0-9]{4})").Groups;
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
                else if (cat is "1018")
                {
                    #region Сериалы
                    name = Regex.Match(title, "^([^/\\(\\[]+) ").Groups[1].Value;
                    if (Regex.IsMatch(name ?? "", "(Сезон|Серии)", RegexOptions.IgnoreCase))
                        continue;

                    originalname = Regex.Match(title, "^[^/\\(\\[]+ / ([^/\\(\\[]+)").Groups[1].Value;
                    if (Regex.IsMatch(originalname, "[а-яА-Я]"))
                        originalname = null;

                    if (string.IsNullOrWhiteSpace(originalname))
                        originalname = null;

                    if (int.TryParse(Regex.Match(title, " \\[([0-9]{4})(,|-| )").Groups[1].Value, out int _yer))
                        relased = _yer;
                    #endregion
                }
                else if (cat is "113" or "114" or "78" or "81" or "82" or "59" or "60" or "62" or "64" or "1019")
                {
                    #region Нестандартные титлы
                    name = Regex.Match(title, "^([^/\\(\\[]+) ").Groups[1].Value;

                    if (int.TryParse(Regex.Match(title, " \\[([0-9]{4})(,|-| )").Groups[1].Value, out int _yer))
                        relased = _yer;

                    if (Regex.IsMatch(name ?? "", "(Сезон|Серии)", RegexOptions.IgnoreCase))
                        continue;
                    #endregion
                }
                #endregion

                if (!string.IsNullOrWhiteSpace(name))
                {
                    // Id новости
                    string id = Match("href=\"[^\"]+/viewtopic.php\\?t=([0-9]+)\"");
                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    // Удаляем торрент где титл не совпадает (возможно обновлен, добавлена серия и т.д)
                    if (tParse.TryGetValue(url, out TorrentDetails _tcache) && _tcache.title != title)
                        tParse.db.TryRemove(url, out _);

                    #region Получаем Magnet
                    string magnet = null;

                    if (tParse.db.ContainsKey(url) && _tcache?.magnet != null)
                    {
                        magnet = _tcache.magnet;
                    }
                    else
                    {
                        var fullNews = await HttpClient.Get(url, useproxy: true);
                        if (fullNews != null)
                            magnet = Regex.Match(fullNews, "<a href=\"(magnet:\\?xt=[^\"]+)\">").Groups[1].Value;
                    }

                    if (string.IsNullOrWhiteSpace(magnet))
                    {
                        allParse = false;
                        continue;
                    }
                    #endregion

                    #region types
                    string[] types = null;
                    switch (cat)
                    {
                        case "99":
                        case "100":
                        case "106":
                        case "105":
                            types = new string[] { "movie" };
                            break;
                        case "1019":
                        case "1018":
                            types = new string[] { "serial" };
                            break;
                        case "1023":
                        case "1024":
                            types = new string[] { "multfilm" };
                            break;
                        case "113":
                        case "114":
                            types = new string[] { "documovie" };
                            break;
                        case "78":
                        case "81":
                        case "82":
                            types = new string[] { "tvshow" };
                            break;
                        case "59":
                        case "60":
                        case "62":
                        case "64":
                            types = new string[] { "docuserial", "documovie" };
                            break;
                    }
                    #endregion

                    int.TryParse(_sid, out int sid);
                    int.TryParse(_pir, out int pir);

                    tParse.AddOrUpdate(new TorrentDetails()
                    {
                        trackerName = "underverse",
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

            return allParse;
        }
        #endregion
    }
}
