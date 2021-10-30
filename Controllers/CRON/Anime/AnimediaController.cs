using System;
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
    [Route("cron/animedia/[action]")]
    public class AnimediaController : BaseController
    {
        #region Parse
        static bool workParse = false;

        async public Task<string> Parse(int page)
        {
            if (workParse)
                return "work";

            workParse = true;

            int countreset = 0;
            reset: bool res = await parsePage(page, DateTime.Now);
            if (!res)
            {
                if (countreset > 2)
                    return "error";

                await Task.Delay(2000);
                countreset++;
                goto reset;
            }

            workParse = false;
            return "ok";
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
                for (int page = 1; page <= 50; page++)
                    await parsePage(page, DateTime.Today.AddDays(-(2 * page)));
            }
            catch { }

            workDevParse = false;
            return "ok";
        }
        #endregion


        #region parsePage
        async Task<bool> parsePage(int page, DateTime createTime)
        {
            string html = await HttpClient.Get($"https://tt.animedia.tv/" + (page > 0 ? $"P{page * 16}" : ""), useproxy: true);
            if (html == null || !html.Contains("id=\"log_in\""))
                return false;

            foreach (string row in tParse.ReplaceBadNames(html).Split("class=\"ads-list__item\"").Skip(1))
            {
                #region Локальный метод - Match
                string Match(string pattern, int index = 1)
                {
                    string res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim());
                    res = Regex.Replace(res, "[\n\r\t ]+", " ");
                    return res.Trim();
                }
                #endregion

                if (string.IsNullOrWhiteSpace(row) || row.Contains("Дорам"))
                    continue;

                #region Данные раздачи
                string url = Match("href=\"https?://tt.animedia.tv/(anime/[^/]+)\" title=\"Подробнее\"");
                string name = Match("class=\"h3 ads-list__item__title\">([^<]+)</a>");
                string originalname = Match("class=\"original-title\">([^<]+)</div>");
                string episodes = Match("class=\"scroller__item__number__font\">(Сери. )?([^<]+)</div>", 2).ToLower();

                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(originalname) || string.IsNullOrWhiteSpace(episodes))
                    continue;

                url = "https://tt.animedia.tv/" + url;
                string title = $"{name} / {originalname} ({episodes})";
                #endregion

                if (!string.IsNullOrWhiteSpace(name))
                {
                    if (!tParse.TryGetValue(url, out TorrentDetails _tcache) || _tcache.title != title)
                    {
                        string fulnews = await HttpClient.Get(url, useproxy: true);
                        if (fulnews == null)
                            continue;

                        if (fulnews.Split("data-toggle=\"tab\"").Length != 2)
                            continue;

                        #region Год выхода
                        int relased = 0;

                        if (_tcache == null)
                        {
                            string _r = Regex.Match(fulnews, "Дата выпуска: <span>с <a [^>]+>([0-9]{4})").Groups[1].Value;
                            if (_tcache == null && !int.TryParse(_r, out relased) || relased == 0)
                                continue;
                        }
                        else
                        {
                            relased = _tcache.relased;
                        }
                        #endregion

                        string sizeName = Regex.Match(fulnews, "class=\"releases-track\">Размер: <span> ([0-9\\.]+) <abbr [^>]+>GB</abbr>").Groups[1].Value;
                        if (!string.IsNullOrWhiteSpace(sizeName))
                            sizeName += " GB";

                        string magnet = Regex.Match(fulnews, "href=\"(magnet:[^\"]+)\"").Groups[1].Value;

                        if (string.IsNullOrWhiteSpace(magnet) || string.IsNullOrWhiteSpace(sizeName))
                            continue;

                        tParse.AddOrUpdate(new TorrentDetails()
                        {
                            trackerName = "animedia",
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
