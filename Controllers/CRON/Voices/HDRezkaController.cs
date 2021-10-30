using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using JacRed.Engine.CORE;
using JacRed.Engine.Parse;
using JacRed.Models.tParse;
using JacRed.Engine;

namespace JacRed.Controllers.CRON
{
    [Route("cron/hdrezka/[action]")]
    public class HDRezkaController : BaseController
    {
        #region Parse
        static bool workParse = false;

        async public Task<string> Parse(int page = 1)
        {
            if (workParse)
                return "work";

            workParse = true;

            string log = "";

            if (await parsePage(page))
                log += $"{page}\n";

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
                for (int page = 1; page <= 74; page++)
                    await parsePage(page);
            }
            catch { }

            workDevParse = false;
            return "ok";
        }
        #endregion


        #region parsePage
        async Task<bool> parsePage(int page)
        {
            string html = await HttpClient.Get($"https://rezka.cc" + (page > 1 ? $"/page/{page}" : ""), useproxy: true);
            if (html == null || !html.Contains("id=\"main_wrapper\""))
                return false;

            foreach (string row in tParse.ReplaceBadNames(html).Split("<a ").Skip(1))
            {
                #region Локальный метод - Match
                string Match(string pattern, int index = 1)
                {
                    string res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim());
                    res = Regex.Replace(res, "[\n\r\t ]+", " ");
                    return res.Trim();
                }
                #endregion

                if (string.IsNullOrWhiteSpace(row) || !row.Contains("class=\"card-item\""))
                    continue;

                string url = Match("href=\"/([^\"]+)\"");
                if (string.IsNullOrWhiteSpace(url))
                    continue;

                #region types
                string[] types = null;
                string type = Match("class=\"card-item-type ([^\" ]+)\"");

                switch (type)
                {
                    case "films":
                        types = new string[] { "movie" };
                        break;
                    case "series":
                        types = new string[] { "serial" };
                        break;
                    case "cartoons":
                        types = new string[] { "multfilm", "multserial" };
                        break;
                    case "animation":
                        types = new string[] { "anime" };
                        break;
                }

                if (types == null)
                    continue;
                #endregion

                if (!string.IsNullOrWhiteSpace(url))
                {
                    url = "https://rezka.cc/" + url;

                    if (!tParse.TryGetValue(url, out TorrentDetails _tcache))
                    {
                        string fulnews = await HttpClient.Get(url, useproxy: true);
                        if (fulnews == null)
                            continue;

                        string name = Regex.Match(fulnews, "class=\"si-title\">([^<]+)<").Groups[1].Value.Split("/")[0].Trim();

                        string siparam = Regex.Match(fulnews, "class=\"si-param\">(s[0-9]+e[0-9]+)", RegexOptions.IgnoreCase).Groups[1].Value;
                        if (string.IsNullOrWhiteSpace(siparam) && type == "series")
                            continue;

                        var g = Regex.Match(fulnews, "<div class=\"si-data\">[\n\r\t ]+<ul>[\n\r\t ]+<li>([^<]+)</li>[\n\r\t ]+<li>([0-9]{4})").Groups;
                        string originalname = g[1].Value.Split("/")[0].Trim();
                        if (!int.TryParse(g[2].Value, out int relased) || relased == 0)
                            continue;

                        #region Дата создания
                        DateTime createTime = tParse.ParseCreateTime(Regex.Match(fulnews, "class=\"si-date\">(Добавлено|Опубликовано) ([^<]+)<").Groups[2].Value, "dd.MM.yyyy");

                        if (createTime == default)
                            continue;
                        #endregion

                        #region Обновляем/Получаем Magnet
                        string magnet = null;
                        string sizeName = null;
                        string quality = null;

                        byte[] torrent = null;

                        foreach (string q in new string[] { "1080p", "720p" })
                        {
                            string tid = Regex.Match(fulnews, $"href=\"/([^\"]+)\" class=\"dwn-links-item\">{q}</a>").Groups[1].Value;
                            if (string.IsNullOrWhiteSpace(tid))
                                continue;

                            torrent = await HttpClient.Download($"https://rezka.cc/{tid}", referer: url, useproxy: true);
                            magnet = BencodeTo.Magnet(torrent);
                            sizeName = BencodeTo.SizeName(torrent);
                            quality = q;

                            if (!string.IsNullOrWhiteSpace(magnet))
                                break;
                        }

                        if (string.IsNullOrWhiteSpace(magnet))
                            continue;
                        #endregion

                        tParse.AddOrUpdate(new TorrentDetails()
                        {
                            trackerName = "hdrezka",
                            types = types,
                            url = url,
                            title = $"{name} / {originalname} {(string.IsNullOrWhiteSpace(siparam) ? "" : $"/ {siparam.ToLower()} ")}[{relased}, {quality}]",
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
