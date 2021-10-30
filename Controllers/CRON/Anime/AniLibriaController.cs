using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JacRed.Engine.CORE;
using JacRed.Engine.Parse;
using JacRed.Models.tParse;
using Microsoft.AspNetCore.Mvc;
using JacRed.Models.tParse.AniLibria;
using JacRed.Engine;

namespace JacRed.Controllers.CRON
{
    [Route("cron/anilibria/[action]")]
    public class AniLibriaController : BaseController
    {
        #region Parse
        static bool workParse = false;

        async public Task<string> Parse()
        {
            if (workParse)
                return "work";

            workParse = true;

            try
            {

                var roots = await HttpClient.Get<List<RootObject>>("http://api.anilibria.tv/v2/getUpdates?limit=-1", MaxResponseContentBufferSize: 200_000_000, timeoutSeconds: 60 * 5, IgnoreDeserializeObject: true);
                if (roots == null || roots.Count == 0)
                    return "root == null";

                foreach (var root in roots)
                {
                    // Дата создания
                    DateTime createTime = new DateTime(1970, 1, 1, 0, 0, 0, 0).AddSeconds(root.updated);

                    foreach (var torrent in root.torrents.list)
                    {
                        if (string.IsNullOrWhiteSpace(root.code) || 480 >= torrent.quality.resolution && string.IsNullOrWhiteSpace(torrent.quality.encoder) && string.IsNullOrWhiteSpace(torrent.url))
                            continue;

                        // Данные раздачи
                        string url = $"anilibria.tv:{root.code}:{torrent.quality.resolution}:{torrent.quality.encoder}";
                        string title = $"{root.names.ru} / {root.names.en} {root.season.year} (s{root.season.code}, e{torrent.series.@string}) [{torrent.quality.@string}]";

                        #region Получаем/Обновляем магнет
                        string magnet = null;
                        string sizeName = null;

                        if (!tParse.TryGetValue(url, out TorrentDetails _tcache) || _tcache.title != title)
                        {
                            byte[] _t = await HttpClient.Download($"https://www.anilibria.tv" + torrent.url, referer: $"https://www.anilibria.tv/release/{root.code}.html", useproxy: true);
                            magnet = BencodeTo.Magnet(_t);

                            if (!string.IsNullOrWhiteSpace(magnet))
                                sizeName = BencodeTo.SizeName(_t);

                            if (string.IsNullOrWhiteSpace(magnet) || string.IsNullOrWhiteSpace(sizeName))
                                continue;
                        }
                        #endregion

                        tParse.AddOrUpdate(new TorrentDetails()
                        {
                            trackerName = "anilibria",
                            types = new string[] { "anime" },
                            url = url,
                            title = title,
                            sid = torrent.seeders,
                            pir = torrent.leechers,
                            createTime = createTime,
                            magnet = magnet,
                            sizeName = sizeName,
                            name = tParse.ReplaceBadNames(root.names.ru),
                            originalname = tParse.ReplaceBadNames(root.names.en),
                            relased = root.season.year
                        });
                    }
                }
            }
            catch { }

            workParse = false;
            return "ok";
        }
        #endregion
    }
}
