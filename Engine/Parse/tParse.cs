using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using JacRed.Engine.CORE;
using JacRed.Models.tParse;

namespace JacRed.Engine.Parse
{
    public static class tParse
    {
        #region tParse
        public static ConcurrentDictionary<string, TorrentDetails> db = new ConcurrentDictionary<string, TorrentDetails>();

        public static ConcurrentDictionary<string, ConcurrentDictionary<string, TorrentDetails>> searchDb = new ConcurrentDictionary<string, ConcurrentDictionary<string, TorrentDetails>>();

        static tParse()
        {
            if (File.Exists("Data/torrents.json") || File.Exists("Data/torrents.json.gz"))
                db = JsonStream.Read<ConcurrentDictionary<string, TorrentDetails>>("Data/torrents.json");

            foreach (var item in db)
                AddOrUpdateSearchDb(item.Value);
        }
        #endregion


        #region ReplaceBadNames
        public static string ReplaceBadNames(string html)
        {
            return html.Replace("Ванда/Вижн ", "ВандаВижн ").Replace("Ё", "Е").Replace("ё", "е").Replace("щ", "ш");
        }
        #endregion

        #region ParseCreateTime
        public static DateTime ParseCreateTime(string line, string format)
        {
            line = Regex.Replace(line, " янв\\.? ", ".01.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " февр?\\.? ", ".02.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " март?\\.? ", ".03.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " апр\\.? ", ".04.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " май\\.? ", ".05.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " июнь?\\.? ", ".06.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " июль?\\.? ", ".07.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " авг\\.? ", ".08.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " сент?\\.? ", ".09.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " окт\\.? ", ".10.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " нояб?\\.? ", ".11.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " дек\\.? ", ".12.", RegexOptions.IgnoreCase);

            line = Regex.Replace(line, " январ(ь|я)?\\.? ", ".01.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " феврал(ь|я)?\\.? ", ".02.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " марта?\\.? ", ".03.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " апрел(ь|я)?\\.? ", ".04.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " май?я?\\.? ", ".05.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " июн(ь|я)?\\.? ", ".06.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " июл(ь|я)?\\.? ", ".07.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " августа?\\.? ", ".08.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " сентябр(ь|я)?\\.? ", ".09.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " октябр(ь|я)?\\.? ", ".10.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " ноябр(ь|я)?\\.? ", ".11.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " декабр(ь|я)?\\.? ", ".12.", RegexOptions.IgnoreCase);

            line = Regex.Replace(line, " Jan ", ".01.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " Feb ", ".02.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " Mar ", ".03.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " Apr ", ".04.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " May ", ".05.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " Jun ", ".06.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " Jul ", ".07.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " Aug ", ".08.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " Sep ", ".09.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " Oct ", ".10.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " Nov ", ".11.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " Dec ", ".12.", RegexOptions.IgnoreCase);

            if (Regex.IsMatch(line, "^[0-9]\\."))
                line = $"0{line}";

            DateTime.TryParseExact(line.ToLower(), format, new CultureInfo("ru-RU"), DateTimeStyles.None, out DateTime createTime);
            return createTime;
        }
        #endregion


        #region AddOrUpdateSearchDb
        static void AddOrUpdateSearchDb(TorrentDetails torrent)
        {
            if ((!string.IsNullOrWhiteSpace(torrent.name) || !string.IsNullOrWhiteSpace(torrent.originalname)) && !string.IsNullOrWhiteSpace(torrent.magnet))
            {
                string search_name = StringConvert.SearchName(torrent.name);
                string search_originalname = StringConvert.SearchName(torrent.originalname);

                string key = $"{search_name}:{search_originalname}";
                if (!searchDb.ContainsKey(key))
                    searchDb.TryAdd(key, new ConcurrentDictionary<string, TorrentDetails>());

                var tdb = searchDb[key];
                tdb.AddOrUpdate(torrent.url, torrent, (k,v) => torrent);
            }
        }
        #endregion


        #region TryGetValue
        public static bool TryGetValue(string url, out TorrentDetails torrent)
        {
            return db.TryGetValue(url, out torrent);
        }
        #endregion

        #region AddOrUpdate
        public static void AddOrUpdate(TorrentDetails torrent)
        {
            if (db.TryGetValue(torrent.url, out TorrentDetails _cache))
            {
                _cache.types = torrent.types;
                _cache.trackerName = torrent.trackerName;
                _cache.title = torrent.title;

                if (!string.IsNullOrWhiteSpace(torrent.magnet))
                    _cache.magnet = torrent.magnet;

                _cache.sid = torrent.sid;
                _cache.pir = torrent.pir;

                if (torrent.size > 0)
                    _cache.size = torrent.size;

                if (!string.IsNullOrWhiteSpace(torrent.sizeName))
                    _cache.sizeName = torrent.sizeName;

                _cache.updateTime = DateTime.Now;

                if (torrent.createTime > _cache.createTime)
                    _cache.createTime = torrent.createTime;

                if (string.IsNullOrWhiteSpace(_cache.originalname))
                    _cache.originalname = torrent.originalname;

                if (_cache.relased == 0)
                    _cache.relased = torrent.relased;

                AddOrUpdateSearchDb(_cache);
            }
            else
            {
                db.TryAdd(torrent.url, torrent);
                AddOrUpdateSearchDb(torrent);
            }
        }
        #endregion


        #region SaveAndUpdateDB
        public static void SaveAndUpdateDB()
        {
            try
            {
                JsonStream.Write("Data/torrents.json", db);
            }
            catch { }
        }
        #endregion
    }
}
