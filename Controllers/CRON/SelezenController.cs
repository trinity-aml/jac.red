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
    [Route("cron/selezen/[action]")]
    public class SelezenController : BaseController
    {
        static List<TaskParse> taskParse = JsonConvert.DeserializeObject<List<TaskParse>>(IO.File.ReadAllText("Data/temp/selezen_taskParse.json"));


        #region Parse
        async public Task<string> Parse(int page = 1)
        {
            int countreset = 0;
            reset: bool res = await parsePage(page);
            if (!res)
            {
                if (countreset > 2)
                    return "error";

                await Task.Delay(2000);
                countreset++;
                goto reset;
            }

            return "ok";
        }
        #endregion

        #region UpdateTasksParse
        async public Task<string> UpdateTasksParse()
        {
            // Получаем html
            string html = await HttpClient.Get("https://selezen.net/", timeoutSeconds: 10, useproxy: true);
            if (html == null)
                return "html == null";

            // Максимальное количиство страниц
            int.TryParse(Regex.Match(html, "<span class=\"nav_ext\">[^<]+</span> <a href=\"[^\"]+\">([0-9]+)</a>").Groups[1].Value, out int maxpages);

            if (maxpages > 0)
            {
                // Загружаем список страниц в список задач
                for (int page = 1; page <= maxpages; page++)
                {
                    if (taskParse.Find(i => i.page == page) == null)
                        taskParse.Add(new TaskParse(page));
                }
            }

            IO.File.WriteAllText("Data/temp/selezen_taskParse.json", JsonConvert.SerializeObject(taskParse));
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
                foreach (var val in taskParse)
                {
                    if (1 >= DateTime.Now.Hour)
                        break;

                    if (DateTime.Today == val.updateTime)
                        continue;

                    bool res = await parsePage(val.page);
                    if (res)
                        val.updateTime = DateTime.Today;
                }
            }
            catch { }

            _parseAllTaskWork = false;
            return "ok";
        }
        #endregion


        #region parsePage
        async Task<bool> parsePage(int page)
        {
            string html = await HttpClient.Get(page == 1 ? "https://selezen.net/relizy-ot-selezen/" : $"https://selezen.net/relizy-ot-selezen/page/{page}/", useproxy: true);
            if (html == null || !html.Contains("<title>Релизы от селезень"))
                return false;

            bool allParse = true;

            foreach (string row in tParse.ReplaceBadNames(html).Split("class=\"card card-default\"").Skip(1))
            {
                if (row.Contains(">Аниме</a>"))
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
                DateTime createTime = tParse.ParseCreateTime(Match("glyphicon-time\"></span> ?([0-9]{2}\\.[0-9]{2}\\.[0-9]{4} [0-9]{2}:[0-9]{2})</a>"), "dd.MM.yyyy HH:mm");

                if (createTime == default)
                    continue;
                #endregion

                #region Данные раздачи
                string url = Match("class=\"short-title\"><a href=\"(https?://[^/]+)?/([^\"]+)\"", 2);
                string title = Match("class=\"short-title\"><a [^>]+>([^<]+)</a>");
                string _sid = Match("<i class=\"fa fa-arrow-up\" [^>]+></i><span [^>]+> ?<b>([0-9]+)</b>");
                string _pir = Match("<i class=\"fa fa-arrow-down\" [^>]+></i><span [^>]+> ?<b>([0-9]+)</b>");
                string sizeName = Match("<i class=\"fa fa-file-video-o\"[^>]+></i> ?<b>([^<]+)</b>");

                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(_sid) || string.IsNullOrWhiteSpace(_pir) || string.IsNullOrWhiteSpace(sizeName))
                    continue;

                url = "http://selezen.net/" + url;
                #endregion

                #region Парсим раздачи
                int relased = 0;
                string name = null, originalname = null;

                // Бэд трип / Приколисты в дороге / Bad Trip (2020)
                var g = Regex.Match(title, "^([^/\\(]+) / [^/]+ / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                {
                    name = g[1].Value;
                    originalname = g[2].Value;

                    if (int.TryParse(g[3].Value, out int _yer))
                        relased = _yer;
                }
                else
                {
                    // Летний лагерь / A Week Away (2021)
                    g = Regex.Match(title, "^([^/\\(]+) / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                    name = g[1].Value;
                    originalname = g[2].Value;

                    if (int.TryParse(g[3].Value, out int _yer))
                        relased = _yer;
                }
                #endregion

                if (!string.IsNullOrWhiteSpace(name))
                {
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
                        string fullnews = await HttpClient.Get(url, cookie: AppInit.selezenCookie, useproxy: true);
                        if (fullnews != null)
                        {
                            string _mg = Regex.Match(fullnews, "href=\"(magnet:\\?xt=urn:btih:[^\"]+)\"").Groups[1].Value;
                            if (!string.IsNullOrWhiteSpace(_mg))
                                magnet = _mg;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(magnet))
                    {
                        allParse = false;
                        continue;
                    }
                    #endregion

                    #region types
                    string[] types = new string[] { "movie" };
                    if (row.Contains(">Мульт") || row.Contains(">мульт"))
                        types = new string[] { "multfilm" };
                    #endregion

                    int.TryParse(_sid, out int sid);
                    int.TryParse(_pir, out int pir);

                    tParse.AddOrUpdate(new TorrentDetails()
                    {
                        trackerName = "selezen",
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
