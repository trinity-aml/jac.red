using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using JacRed.Engine.CORE;
using JacRed.Engine.Parse;
using JacRed.Models.tParse;
using IO = System.IO;
using JacRed.Engine;

namespace JacRed.Controllers.CRON
{
    [Route("cron/toloka/[action]")]
    public class TolokaController : BaseController
    {
        static Dictionary<string, List<TaskParse>> taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(IO.File.ReadAllText("Data/temp/toloka_taskParse.json"));

        #region Cookie / TakeLogin
        static string Cookie(IMemoryCache memoryCache)
        {
            if (memoryCache.TryGetValue("cron:TolokaController:Cookie", out string cookie))
                return cookie;

            return null;
        }

        async static Task<bool> TakeLogin(IMemoryCache memoryCache)
        {
            try
            {
                var clientHandler = new System.Net.Http.HttpClientHandler()
                {
                    AllowAutoRedirect = false
                };

                using (var client = new System.Net.Http.HttpClient(clientHandler))
                {
                    client.MaxResponseContentBufferSize = 2000000; // 2MB
                    client.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/75.0.3770.100 Safari/537.36");

                    var postParams = new Dictionary<string, string>();
                    postParams.Add("username", AppInit.tolokaLogin.u);
                    postParams.Add("password", AppInit.tolokaLogin.p);
                    postParams.Add("autologin", "on");
                    postParams.Add("ssl", "on");
                    postParams.Add("redirect", "index.php?");
                    postParams.Add("login", "Вхід");

                    using (var postContent = new System.Net.Http.FormUrlEncodedContent(postParams))
                    {
                        using (var response = await client.PostAsync("https://toloka.to/login.php", postContent))
                        {
                            if (response.Headers.TryGetValues("Set-Cookie", out var cook))
                            {
                                string toloka_sid = null, toloka_data = null;
                                foreach (string line in cook)
                                {
                                    if (string.IsNullOrWhiteSpace(line))
                                        continue;

                                    if (line.Contains("toloka_sid="))
                                        toloka_sid = new Regex("toloka_sid=([^;]+)(;|$)").Match(line).Groups[1].Value;

                                    if (line.Contains("toloka_data="))
                                        toloka_data = new Regex("toloka_data=([^;]+)(;|$)").Match(line).Groups[1].Value;
                                }

                                if (!string.IsNullOrWhiteSpace(toloka_sid) && !string.IsNullOrWhiteSpace(toloka_data))
                                {
                                    memoryCache.Set("cron:TolokaController:Cookie", $"toloka_sid={toloka_sid}; toloka_ssl=1; toloka_data={toloka_data};", TimeSpan.FromHours(1));
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return false;
        }
        #endregion


        #region Parse
        async public Task<string> Parse(int page = 0)
        {
            string log = "";

            foreach (string cat in new List<string>() { "16", "96", "19", "139", "32", "173", "174", "44" })
            {
                await parsePage(cat, page, parseMagnet: true);
                log += $"{cat} - {page}\n";
            }

            return string.IsNullOrWhiteSpace(log) ? "ok" : log;
        }
        #endregion

        #region UpdateTasksParse
        async public Task<string> UpdateTasksParse(bool saveDb)
        {
            if (saveDb)
            {
                IO.File.WriteAllText("Data/temp/toloka_taskParse.json", JsonConvert.SerializeObject(taskParse));
                return "save";
            }

            #region Авторизация
            if (Cookie(memoryCache) == null)
            {
                string authKey = "toloka:TakeLogin()";
                if (memoryCache.TryGetValue(authKey, out _))
                    return "TakeLogin == null";

                if (await TakeLogin(memoryCache) == false)
                {
                    memoryCache.Set(authKey, 0, TimeSpan.FromMinutes(5));
                    return "TakeLogin == null";
                }
            }
            #endregion

            foreach (string cat in new List<string>() 
            { 
                // Українське озвучення
                "16", "32",  "19", "44",

                // HD українською
                "96", "173", "139", "174", "140",

                // Документальні фільми українською
                "230", "226", "227", "228", "229"
            })
            {
                // Получаем html
                string html = await HttpClient.Get($"https://toloka.to/f{cat}", timeoutSeconds: 10, cookie: Cookie(memoryCache));
                if (html == null)
                    continue;

                // Максимальное количиство страниц
                int.TryParse(Regex.Match(html, "<a href=\"[^\"]+\">([0-9]+)</a>&nbsp;&nbsp;<a href=\"[^\"]+\">наступна</a>").Groups[1].Value, out int maxpages);

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
            #region Авторизация
            if (Cookie(memoryCache) == null)
            {
                string authKey = "toloka:TakeLogin()";
                if (memoryCache.TryGetValue(authKey, out _))
                    return false;

                if (await TakeLogin(memoryCache) == false)
                {
                    memoryCache.Set(authKey, 0, TimeSpan.FromMinutes(5));
                    return false;
                }
            }
            #endregion

            string html = await HttpClient.Get("https://toloka.to" + $"/f{cat}{(page == 0 ? "" : $"-{page * 45}")}", cookie: Cookie(memoryCache)/*, useproxy: true, proxy: tParse.webProxy()*/);
            if (html == null || !html.Contains("<html lang=\"uk\""))
                return false;

            foreach (string row in tParse.ReplaceBadNames(html).Split("<td class=\"row1\" width=\"100%\">").Skip(1))
            {
                if (string.IsNullOrWhiteSpace(row) || Regex.IsMatch(row, "Збір коштів", RegexOptions.IgnoreCase))
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
                string _createTime = Match("class=\"postdetails\">([0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}:[0-9]{2})").Replace("-", ".");
                if (!DateTime.TryParse(_createTime, out DateTime createTime) || createTime == default)
                    continue;
                #endregion

                #region Данные раздачи
                string url = Match("<a href=\"(t[0-9]+)\" class=\"topictitle\"");
                string title = Match("class=\"topictitle\">([^<]+)</a>");

                string _sid = Match("<span class=\"seedmed\" [^>]+><b>([0-9]+)</b></span>");
                string _pir = Match("<span class=\"leechmed\" [^>]+><b>([0-9]+)</b></span>");
                string sizeName = Match("<a href=\"download.php[^\"]+\" [^>]+>([^<]+)</a>").Replace("&nbsp;", " ");

                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(_sid) || string.IsNullOrWhiteSpace(_pir) || string.IsNullOrWhiteSpace(sizeName) || sizeName == "0 B")
                    continue;

                url = "http://toloka.to/" + url;
                #endregion

                #region Парсим раздачи
                int relased = 0;
                string name = null, originalname = null;

                if (cat is "16" or "96" or "19" or "139")
                {
                    #region Фильмы
                    // Незворотність / Irréversible / Irreversible (2002) AVC Ukr/Fre | Sub Eng
                    var g = Regex.Match(title, "^([^/\\(\\[]+) / [^/\\(\\[]+ / ([^/\\(\\[]+) \\(([0-9]{4})(\\)|-)").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                    {
                        name = g[1].Value;
                        originalname = g[2].Value;

                        if (int.TryParse(g[3].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        // Мій рік у Нью-Йорку / My Salinger Year (2020) Ukr/Eng
                        g = Regex.Match(title, "^([^/\\(\\[]+) / ([^/\\(\\[]+) \\(([0-9]{4})(\\)|-)").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                        {
                            name = g[1].Value;
                            originalname = g[2].Value;

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                    }
                    #endregion
                }
                else if (cat is "32" or "173" or "174" or "44" or "230" or "226" or "227" or "228" or "229")
                {
                    #region Сериалы
                    // Дім з прислугою (Сезон 2, серії 1-8) / Servant (Season 2, episodes 1-8) (2021) WEB-DLRip-AVC Ukr/Eng
                    var g = Regex.Match(title, "^([^/\\(\\[]+) (\\([^\\)]+\\) )?/ ([^/\\(\\[]+) (\\([^\\)]+\\) )?\\(([0-9]{4})(\\)|-)").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[3].Value) && !string.IsNullOrWhiteSpace(g[5].Value))
                    {
                        name = g[1].Value;
                        originalname = g[3].Value;

                        if (int.TryParse(g[5].Value, out int _yer))
                            relased = _yer;
                    }
                    #endregion
                }
                #endregion

                if (!string.IsNullOrWhiteSpace(name))
                {
                    // Id новости
                    string id = Match("<a href=\"t([0-9]+)\" class=\"topictitle\"");
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
                            string downloadId = Regex.Match(row, "href=\"download.php\\?id=([0-9]+)\"").Groups[1].Value;
                            if (!string.IsNullOrWhiteSpace(downloadId))
                            {
                                byte[] torrent = await HttpClient.Download($"https://toloka.to/download.php?id={downloadId}", cookie: Cookie(memoryCache), referer: "https://toloka.to");
                                magnet = BencodeTo.Magnet(torrent);
                            }
                        }
                    }
                    #endregion

                    #region types
                    string[] types = null;
                    switch (cat)
                    {
                        case "16":
                        case "96":
                            types = new string[] { "movie" };
                            break;
                        case "19":
                        case "139":
                            types = new string[] { "multfilm" };
                            break;
                        case "32":
                        case "173":
                            types = new string[] { "serial" };
                            break;
                        case "174":
                        case "44":
                            types = new string[] { "multserial" };
                            break;
                        case "226":
                        case "227":
                        case "228":
                        case "229":
                        case "230":
                            types = new string[] { "docuserial", "documovie" };
                            break;
                    }
                    #endregion

                    int.TryParse(_sid, out int sid);
                    int.TryParse(_pir, out int pir);

                    tParse.AddOrUpdate(new TorrentDetails()
                    {
                        trackerName = "toloka",
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
            #region Авторизация
            if (Cookie(memoryCache) == null)
            {
                string authKey = "toloka:TakeLogin()";
                if (memoryCache.TryGetValue(authKey, out _))
                    return "TakeLogin == false";

                if (await TakeLogin(memoryCache) == false)
                {
                    memoryCache.Set(authKey, 0, TimeSpan.FromMinutes(5));
                    return "TakeLogin == false";
                }
            }
            #endregion

            if (_parseMagnetWork)
                return "work";

            _parseMagnetWork = true;

            try
            {
                foreach (var torrent in tParse.db.Where(i => i.Value.trackerName == "toloka" && string.IsNullOrWhiteSpace(i.Value.magnet)))
                {
                    string html = await HttpClient.Get(torrent.Key, cookie: Cookie(memoryCache));

                    if (html != null)
                    {
                        string downloadId = Regex.Match(html, "href=\"download.php\\?id=([0-9]+)\"").Groups[1].Value;
                        if (!string.IsNullOrWhiteSpace(downloadId))
                        {
                            string magnet = BencodeTo.Magnet(await HttpClient.Download($"https://toloka.to/download.php?id={downloadId}", cookie: Cookie(memoryCache), referer: "https://toloka.to"));

                            if (!string.IsNullOrWhiteSpace(magnet))
                                torrent.Value.magnet = magnet;
                        }
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
