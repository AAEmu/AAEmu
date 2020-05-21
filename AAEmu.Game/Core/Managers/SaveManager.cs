using System;
using System.Diagnostics;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Tasks.SaveTask;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class SaveManager : Singleton<SaveManager>
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();

        private const double Delay = 1; // TODO: 1 minute for debugging, should likely be less on production, maybe add to configuration ?
        private bool _enabled;
        private bool _isSaving;

        public SaveManager()
        {
            _enabled = false;
            _isSaving = false;
        }

        public void Initialize()
        {
            _log.Info("Initialising Save Manager...");
            _enabled = true;
            SaveTickStart();
        }

        public void Stop()
        {
            _enabled = false;
        }

        public void SaveTickStart()
        {
            _log.Warn("SaveTickStart: Started");

            var saveTask = new SaveTickStartTask();
            TaskManager.Instance.Schedule(saveTask, TimeSpan.FromMinutes(Delay), TimeSpan.FromMinutes(Delay));
        }

        public bool DoSave()
        {
            if (_isSaving)
                return false;
            _isSaving = true;
            var stopWatch = new Stopwatch();
            stopWatch.Start();
            bool saved = false;
            try
            {
                // Save stuff
                _log.Info("Saving DB ...");
                HousingManager.Instance.Save();
                MailManager.Instance.Save();
                ItemManager.Instance.Save();
                // Save Characters
                var cCount = 0;
                foreach (var c in WorldManager.Instance.GetAllCharacters())
                {
                    if (c.Save())
                        cCount++;
                    else
                        _log.Error("Failed to save character {0} - {1}", c.Id, c.Name);
                }
                _log.Info("Update {0} characters ...",cCount);
                saved = true;
            }
            catch (Exception e)
            {
                _log.Error(string.Format("DoSave - Exception: {0}", e.Message));
            }
            _isSaving = false;
            stopWatch.Stop();
            _log.Info("Saving data took {0}", stopWatch.Elapsed);
            return saved;
        }


        public void SaveTick()
        {
            if (!_enabled)
            {
                _log.Warn("SaveTickStart: not enabled, skipping ...");
                return;
            }
            DoSave();
        }

    }
}
