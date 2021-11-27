using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using JacRed.Models.TorrServer;

namespace JacRed.Engine.CORE
{
    public static class TorrServerAPI
    {
        #region TorrServerAPI
        static bool saveDb = false;

        public static ConcurrentDictionary<string, List<MediaInfo>> db = null;

        static TorrServerAPI()
        {
            db = JsonStream.Read<ConcurrentDictionary<string, List<MediaInfo>>>("Data/temp/torfiles.json");
        }
        #endregion

        #region Lock MediaFiles
        async public static ValueTask<List<MediaInfo>> MediaFiles(IMemoryCache memoryCache, string magnet)
        {
            #region hash
            string hash = magnet;

            if (hash.Contains("magnet:?xt=urn"))
                hash = Regex.Match(hash, "urn:btih:([a-zA-Z0-9]+)").Groups[1].Value;

            hash = hash.ToLower();
            #endregion

            // Отдаем кеш
            if (db.TryGetValue(hash, out List<MediaInfo> _cache))
                return _cache;

            string _mk = $"torapi:MediaFiles:lock:{hash}";
            if (memoryCache.TryGetValue(_mk, out _))
                return null;

            memoryCache.Set(_mk, true, DateTime.Now.AddSeconds(70));

            var medias = await MediaFiles(magnet);
            if (medias == null)
                return null;

            memoryCache.Remove(_mk);

            saveDb = true;
            db.TryAdd(hash, medias);
            return medias;
        }
        #endregion

        #region MediaFiles
        async static Task<List<MediaInfo>> MediaFiles(string magnet)
        {
            try
            {
                var medias = new List<MediaInfo>();
                string torhost = "127.0.0.1:8090";

                #region Добовляем торрент
                string hash = await HttpClient.Post($"http://{torhost}/torrents", "{\"action\":\"add\",\"link\":\"" + HttpUtility.UrlDecode(magnet) + "\",\"title\":\"\",\"poster\":\"\",\"save_to_db\":false}", timeoutSeconds: 8);
                if (hash == null)
                    return null;

                hash = Regex.Match(hash, "\"hash\":\"([^\"]+)\"").Groups[1].Value;
                if (string.IsNullOrWhiteSpace(hash))
                    return null;
                #endregion

                #region Получаем список файлов
                Stat stat = null;
                DateTime startGotInfoTime = DateTime.Now;

                resetgotingo: stat = await HttpClient.Post<Stat>($"http://{torhost}/torrents", "{\"action\":\"get\",\"hash\":\"" + hash + "\"}", timeoutSeconds: 3);
                if (stat?.file_stats == null || stat.file_stats.Count == 0)
                {
                    if (DateTime.Now > startGotInfoTime.AddSeconds(70))
                    {
                        _ = await HttpClient.Post($"http://{torhost}/torrents", "{\"action\":\"rem\",\"hash\":\"" + hash + "\"}", timeoutSeconds: 2);
                        return null;
                    }

                    await Task.Delay(300);
                    goto resetgotingo;
                }
                #endregion

                // Удаляем торрент из базы
                _ = await HttpClient.Post($"http://{torhost}/torrents", "{\"action\":\"rem\",\"hash\":\"" + hash + "\"}", timeoutSeconds: 2);

                // Генерируем список файлов
                foreach (var file in stat.file_stats.OrderBy(i => i.Id))
                {
                    medias.Add(new MediaInfo()
                    {
                        Id = file.Id,
                        Path = Path.GetFileName(file.Path),
                        FileSize = file.Length
                    });
                }

                // Успех
                return medias;
            }
            catch
            {
                return null;
            }
        }
        #endregion


        #region SaveDB
        public static void SaveDB()
        {
            if (!saveDb)
                return;

            saveDb = false;

            try
            {
                // Сохраняем кеш
                JsonStream.Write("Data/temp/torfiles.json", db);
            }
            catch 
            {
                saveDb = true;
            }
        }
        #endregion
    }
}
