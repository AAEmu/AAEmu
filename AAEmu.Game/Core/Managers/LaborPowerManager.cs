using System;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Tasks.LaborPower;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class LaborPowerManager : Singleton<LaborPowerManager>
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();

        //private List<LaborPower> _onlineChar;
        //private List<LaborPower> _offlineChar;
        private const short LpChangePremium = 10; // TODO in config
        private const short LpChange = 5;
        private const short UpLimit = 2000;
        private const double Delay = 5; // min

        public LaborPowerManager()
        {
            //_onlineChar = new List<LaborPower>();
            //_offlineChar = new List<LaborPower>();
        }

        public void Initialize()
        {
            _log.Info("Initialising Labor Power Manager...");
            LaborPowerTickStart();
        }

        public void LaborPowerTickStart()
        {
            _log.Warn("LaborPowerTickStart: Started");

            var lpTickStartTask = new LaborPowerTickStartTask();
            TaskManager.Instance.Schedule(lpTickStartTask, TimeSpan.FromMinutes(Delay), TimeSpan.FromMinutes(Delay));
        }
        public void LaborPowerTick()
        {
            var connections = GameConnectionTable.Instance.GetConnections();
            foreach (var connection in connections)
            {
                foreach (var character in connection.Characters)
                {
                    if (!character.Value.IsOnline)
                    {
                        continue;
                    }
                    if (character.Value.LaborPower >= UpLimit)
                    {
                        _log.Warn("No need to increase Labor Point, since they reached the limit {0} for Char: {1}", UpLimit, character.Value.Name);
                        continue;
                    }
                    var change = (short)(UpLimit - character.Value.LaborPower);
                    if (change >= LpChange)
                    {
                        _log.Warn("Added {0} Labor Point for Char: {1}", LpChange, character.Value.Name);
                        character.Value.LaborPowerModified = DateTime.Now;
                        character.Value.ChangeLabor(LpChange, 0);
                    }
                    else if (change != 0)
                    {
                        _log.Warn("Added {0} Labor Point for Char: {1}", change, character.Value.Name);
                        character.Value.LaborPowerModified = DateTime.Now;
                        character.Value.ChangeLabor(change, 0);
                    }
                }
            }
        }

    }
}
