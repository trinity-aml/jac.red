using Microsoft.AspNetCore.Mvc;
using JacRed.Engine.Parse;
using JacRed.Engine;
using JacRed.Engine.CORE;

namespace JacRed.Controllers
{
    [Route("jsondb/[action]")]
    public class DbController : BaseController
    {
        static bool _saveDbWork = false;

        public string Save()
        {
            if (_saveDbWork)
                return "work";

            _saveDbWork = true;

            try
            {
                tParse.SaveAndUpdateDB();
                TorrServerAPI.SaveDB();
            }
            catch { }

            _saveDbWork = false;
            return "ok";
        }
    }
}
