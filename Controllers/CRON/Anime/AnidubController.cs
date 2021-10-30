using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using JacRed.Engine;
using JacRed.Engine.CORE;
using JacRed.Engine.Parse;
using JacRed.Models.tParse;
using Microsoft.AspNetCore.Mvc;

namespace JacRed.Controllers.CRON
{
    [Route("cron/anidub/[action]")]
    public class AnidubController : BaseController
    {
        #region Parse
        static bool workParse = false;

        async public Task<string> Parse(int page = 1)
        {
            if (workParse)
                return "work";

            workParse = true;

            string log = "";

            // Законченные "anime_tv/full"
            foreach (string cat in new List<string>() { "anime_tv/anime_ongoing", "anime_tv/shonen", "anime_ova", "anime_movie" })
            {
                int countreset = 0;
                reset: bool res = await parsePage(cat, page);
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

            workParse = false;
            return string.IsNullOrWhiteSpace(log) ? "ok" : log;
        }
        #endregion

        #region DevParse
        static bool workDevParse = false;

        async public Task<string> DevParse()
        {
            if (workDevParse)
                return "work";

            workDevParse = true;

            try
            {
                for (int page = 1; page <= 4; page++)
                    await parsePage("anime_tv/anime_ongoing", page);

                for (int page = 1; page <= 41; page++)
                    await parsePage("anime_ova", page);

                for (int page = 1; page <= 22; page++)
                    await parsePage("anime_movie", page);

                for (int page = 1; page <= 124; page++)
                    await parsePage("anime_tv/full", page);
            }
            catch { }

            workDevParse = false;
            return "ok";
        }
        #endregion


        #region parsePage
        async Task<bool> parsePage(string cat, int page)
        {
            string html = await HttpClient.Get($"https://tr.anidub.com/{cat}/" + (page > 1 ? $"page/{page}/" : ""), useproxy: true);
            if (html == null || !html.Contains("id=\"header_h\""))
                return false;

            foreach (string row in tParse.ReplaceBadNames(html).Split("<article class=\"story\"").Skip(1))
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

                if (row.Contains("<b>Дата:</b> Сегодня"))
                {
                    createTime = DateTime.Today;
                }
                else if (row.Contains("<b>Дата:</b> Вчера"))
                {
                    createTime = DateTime.Today.AddDays(-1);
                }
                else
                {
                    createTime = tParse.ParseCreateTime(Match("b>Дата:</b> ([0-9-]+),").Replace("-", "."), "dd.MM.yyyy");
                }

                if (createTime == default)
                    continue;
                #endregion

                #region Данные раздачи
                string url = Match("<h2><a href=\"(https?://[^/]+)?/([^\":]+)\"", 2);
                string title = Match(">([^<]+)</a></h2>");

                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title))
                    continue;

                url = "https://tr.anidub.com/" + url;
                #endregion

                #region name / originalname
                string name = null, originalname = null;

                // Диназенон / SSSS.Dynazenon [07 из 12]
                var g = Regex.Match(title, "^([^/\\[]+) / ([^/\\[]+) \\[").Groups;
                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                {
                    name = g[1].Value;
                    originalname = g[2].Value;
                }
                #endregion

                // Год выхода
                if (!int.TryParse(Match("<b>Год: </b><span><a href=\"[^\"]+\">([0-9]{4})</a>"), out int relased) || relased == 0)
                    continue;

                if (!string.IsNullOrWhiteSpace(name))
                {
                    if (!tParse.TryGetValue(url, out TorrentDetails _tcache) || _tcache.title != title)
                    {
                        #region Обновляем/Получаем Magnet
                        string magnet = null;
                        string sizeName = null;

                        string fulnews = await HttpClient.Get(url, useproxy: true);
                        if (fulnews == null)
                            continue;

                        string tid = Regex.Match(fulnews, "<div class=\"torrent_h\">[\n\r\t ]+<a href=\"/(engine/download.php\\?id=[0-9]+)\"").Groups[1].Value;

                        byte[] torrent = await HttpClient.Download($"https://tr.anidub.com/{tid}", referer: url, useproxy: true);
                        magnet = BencodeTo.Magnet(torrent);
                        sizeName = BencodeTo.SizeName(torrent);

                        if (string.IsNullOrWhiteSpace(magnet))
                            continue;
                        #endregion

                        tParse.AddOrUpdate(new TorrentDetails()
                        {
                            trackerName = "anidub",
                            types = new string[] { "anime" },
                            url = url,
                            title = title,
                            sid = 1,
                            sizeName = sizeName,
                            createTime = createTime,
                            magnet = magnet,
                            name = name,
                            originalname = originalname,
                            relased = relased
                        });
                    }
                }
            }

            return true;
        }
        #endregion
    }
}
