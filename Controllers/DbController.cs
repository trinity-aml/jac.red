using Microsoft.AspNetCore.Mvc;
using JacRed.Engine.Parse;
using JacRed.Engine;

namespace JacRed.Controllers
{
    [Route("jsondb/[action]")]
    public class DbController : BaseController
    {
        #region Save
        static bool _saveDbWork = false;
        static bool _disabledSaveDb = false;

        public string Save(bool forced)
        {
            if (forced)
                _disabledSaveDb = true;

            if (!forced && _disabledSaveDb)
                return "disabled SaveDb";

            if (_saveDbWork)
                return "work";

            _saveDbWork = true;

            try
            {
                tParse.SaveAndUpdateDB();
            }
            catch { }

            _saveDbWork = false;
            return "ok";
        }
        #endregion
    }
}
