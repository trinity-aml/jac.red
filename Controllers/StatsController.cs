using System;
using System.Collections.Generic;
using System.Linq;
using JacRed.Engine.Parse;
using Microsoft.AspNetCore.Mvc;

namespace JacRed.Controllers
{
    [Route("stats/[action]")]
    public class StatsController : Controller
    {
        #region Torrents
        public JsonResult Torrents(string trackerName)
        {
            if (string.IsNullOrWhiteSpace(trackerName))
            {
                var _s = new List<dynamic>();

                foreach (string tname in tParse.db.Values.Select(i => i.trackerName).ToHashSet())
                {
                    var torrents = tParse.db.Values.Where(i => i.trackerName == tname);

                    _s.Add(new
                    {
                        trackerName = tname,
                        lastnewtor = torrents.OrderByDescending(i => i.createTime).FirstOrDefault()?.createTime.ToString("dd.MM.yyyy"),
                        newtor = torrents.Count(i => i.createTime >= DateTime.Today),
                        update = torrents.Count(i => i.updateTime >= DateTime.Today),
                        actual = torrents.Count(i => i.updateTime >= DateTime.Today.AddDays(-20)),
                        nullParse = torrents.Count(i => i.magnet == null),
                        alltorrents = torrents.Count(),
                    });
                }

                return Json(_s.OrderByDescending(i => i.alltorrents));
            }
            else
            {
                //return Json(tParse.db.Reverse().Where(i => i.Value.trackerName == trackerName && i.Value.magnet == null).Take(100));

                var torrents = tParse.db.Values.Where(i => i.trackerName == trackerName);

                return Json(new
                {
                    lastnewtor = torrents.OrderByDescending(i => i.createTime).FirstOrDefault()?.createTime.ToString("dd.MM.yyyy"),
                    newtor = torrents.Count(i => i.createTime >= DateTime.Today),
                    update = torrents.Count(i => i.updateTime >= DateTime.Today),
                    actual = torrents.Count(i => i.updateTime >= DateTime.Today.AddDays(-20)),
                    alltorrents = torrents.Count(),
                    nullParse = torrents.Where(i => i.magnet == null).OrderByDescending(i => i.createTime).Select(i =>
                    {
                        return new
                        {
                            i.trackerName,
                            i.types,
                            i.url,

                            i.title,
                            i.sid,
                            i.pir,
                            i.size,
                            i.sizeName,
                            i.createTime,
                            i.updateTime,
                            i.magnet,

                            i.name,
                            i.originalname,
                            i.relased
                        };
                    }),
                    lastToday = torrents.Where(i => i.createTime >= DateTime.Today).OrderByDescending(i => i.createTime).Select(i =>
                    {
                        return new
                        {
                            i.trackerName,
                            i.types,
                            i.url,

                            i.title,
                            i.sid,
                            i.pir,
                            i.size,
                            i.sizeName,
                            i.createTime,
                            i.updateTime,
                            i.magnet,

                            i.name,
                            i.originalname,
                            i.relased
                        };
                    }),
                    lastCreateTime = torrents.OrderByDescending(i => i.createTime).Take(40).Select(i =>
                    {
                        return new
                        {
                            i.trackerName,
                            i.types,
                            i.url,

                            i.title,
                            i.sid,
                            i.pir,
                            i.size,
                            i.sizeName,
                            i.createTime,
                            i.updateTime,
                            i.magnet,

                            i.name,
                            i.originalname,
                            i.relased
                        };
                    })
                });
            }
        }
        #endregion
    }
}
