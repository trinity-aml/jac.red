using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using JacRed.Engine.CORE;
using JacRed.Engine.Parse;
using JacRed.Models.tParse;
using System.Collections.Generic;
using JacRed.Engine;

namespace JacRed.Controllers.CRON
{
    [Route("cron/baibako/[action]")]
    public class BaibakoController : BaseController
    {
        static string cookie { get; set; }

        #region TakeLogin
        async public static Task<bool> TakeLogin()
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
                    postParams.Add("username", AppInit.baibakoLogin.u);
                    postParams.Add("password", AppInit.baibakoLogin.p);

                    using (var postContent = new System.Net.Http.FormUrlEncodedContent(postParams))
                    {
                        using (var response = await client.PostAsync("http://baibako.tv/takelogin.php", postContent))
                        {
                            if (response.Headers.TryGetValues("Set-Cookie", out var cook))
                            {
                                string sessid = null, pass = null, uid = null;
                                foreach (string line in cook)
                                {
                                    if (string.IsNullOrWhiteSpace(line))
                                        continue;

                                    if (line.Contains("PHPSESSID="))
                                        sessid = new Regex("PHPSESSID=([^;]+)(;|$)").Match(line).Groups[1].Value;

                                    if (line.Contains("pass="))
                                        pass = new Regex("pass=([^;]+)(;|$)").Match(line).Groups[1].Value;

                                    if (line.Contains("uid="))
                                        uid = new Regex("uid=([^;]+)(;|$)").Match(line).Groups[1].Value;
                                }

                                if (!string.IsNullOrWhiteSpace(sessid) && !string.IsNullOrWhiteSpace(uid) && !string.IsNullOrWhiteSpace(pass))
                                {
                                    cookie = $"PHPSESSID={sessid}; uid={uid}; pass={pass}";
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
        static bool workParse = false;

        async public Task<string> Parse(int page)
        {
            #region Авторизация
            if (cookie == null)
            {
                if (await TakeLogin() == false)
                    return "Не удалось авторизоваться";
            }
            #endregion

            if (workParse)
                return "work";

            workParse = true;

            if (page > 0)
            {
                await parsePage(page);
            }
            else
            {
                await parsePage(0);
                await parsePage(1);
            }

            workParse = false;
            return "ok";
        }
        #endregion

        #region DevParse
        static bool workDevParse = false;

        async public Task<string> DevParse()
        {
            #region Авторизация
            if (cookie == null)
            {
                if (await TakeLogin() == false)
                    return "Не удалось авторизоваться";
            }
            #endregion

            if (workDevParse)
                return "work";

            workDevParse = true;

            try
            {
                for (int page = 1; page <= 418; page++)
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
            string html = await HttpClient.Get($"http://baibako.tv/browse.php?page={page}", encoding: Encoding.GetEncoding(1251), cookie: cookie);
            if (html == null || !html.Contains("id=\"navtop\""))
                return false;

            foreach (string row in tParse.ReplaceBadNames(HttpUtility.HtmlDecode(html.Replace("&nbsp;", ""))).Split("<tr").Skip(1))
            {
                #region Локальный метод - Match
                string Match(string pattern, int index = 1)
                {
                    string res = new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim();
                    res = Regex.Replace(res, "[\n\r\t ]+", " ");
                    return res.Trim();
                }
                #endregion

                if (string.IsNullOrWhiteSpace(row))
                    continue;

                // Дата создания
                DateTime createTime = tParse.ParseCreateTime(Match("<small>Загружена: ([0-9]+ [^ ]+ [0-9]{4}) в [^<]+</small>"), "dd.MM.yyyy");
                if (createTime == default)
                    continue;

                #region Данные раздачи
                var gurl = Regex.Match(row, "<a href=\"/?(details.php\\?id=[0-9]+)[^\"]+\">([^<]+)</a>").Groups;

                string url = gurl[1].Value;
                string title = gurl[2].Value;
                title = title.Replace("(Обновляемая)", "").Replace("(Золото)", "");
                title = Regex.Replace(title, "/( +| )?$", "").Trim();

                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title) || !Regex.IsMatch(title, "(1080p|720p)"))
                    continue;

                url = "http://baibako.tv/" + url;
                #endregion

                #region name / originalname
                string name = null, originalname = null;

                // 9-1-1 /9-1-1 /s04e01-13 /WEBRip XviD
                var g = Regex.Match(title, "([^/\\(]+)[^/]+/([^/\\(]+)").Groups;
                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                {
                    name = g[1].Value.Trim();
                    originalname = g[2].Value.Trim();
                }
                #endregion

                // Год выхода
                int relased = 0;

                if (!string.IsNullOrWhiteSpace(name))
                {
                    if (!tParse.TryGetValue(url, out TorrentDetails _tcache) || _tcache.title != title)
                    {
                        #region Обновляем/Получаем Magnet
                        string magnet = null;
                        string sizeName = null;

                        byte[] torrent = await HttpClient.Download("http://baibako.tv/" + Match("href=\"/?(download.php\\?id=([0-9]+))\""), cookie: cookie);
                        magnet = BencodeTo.Magnet(torrent);
                        sizeName = BencodeTo.SizeName(torrent);

                        if (string.IsNullOrWhiteSpace(magnet) || string.IsNullOrWhiteSpace(sizeName))
                            continue;
                        #endregion

                        tParse.AddOrUpdate(new TorrentDetails()
                        {
                            trackerName = "baibako",
                            types = new string[] { "serial" },
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
