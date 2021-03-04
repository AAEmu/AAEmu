using System;
using AAEmu.Commons.Utils;
using System.Collections.Generic;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Tasks.LaborPower;
using AAEmu.Game.Models;
using Newtonsoft.Json;
using NLog;
using System.IO;

namespace AAEmu.Game.Core.Managers
{
    public class LaborPowerManager : Singleton<LaborPowerManager>
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();

        //private List<LaborPower> _onlineChar;
        //private List<LaborPower> _offlineChar;

        private Dictionary<string, short> _labor;

        public LaborPowerManager()
        {
            //_onlineChar = new List<LaborPower>();
            //_offlineChar = new List<LaborPower>();
        }

        public void Initialize()
        {
            _log.Info("Initialising Labor Power Manager...");
            
            #region FileManager
            _labor = new Dictionary<string, short>();
            Dictionary<string, short> d = new Dictionary<string, short>();
            try
            {
                string data = File.ReadAllText("Data/labor.json");
                d = JsonConvert.DeserializeObject<Dictionary<string, short>>(data);
                foreach (var entry in d)
                    _labor.Add(entry.Key,entry.Value);
            }
            catch (Exception e ){
                _log.Error(e.Message);
            }
            #endregion

            _log.Info("Fremium max labor: {0}", _labor["fremium_labor_maximum"]);
            _log.Info("Premium max labor: {0}", _labor["premium_labor_maximum"]);
            _log.Info("Fremium labor gain: {0}", _labor["fremium_labor_change"]);
            _log.Info("Premium labor gain: {0}", _labor["premium_labor_change"]);
            _log.Info("Fremium offline gain: {0}", _labor["fremium_offline_tick"]);
            _log.Info("Premium offline gain: {0}", _labor["premium_offline_tick"]);

            LaborPowerTickStart();
        }

        public void LaborPowerTickStart()
        {
            _log.Warn("LaborPowerTickStart: Started");

            var lpTickStartTask = new LaborPowerTickStartTask();
            TaskManager.Instance.Schedule(lpTickStartTask, TimeSpan.FromMinutes(_labor["minute_tick"]), TimeSpan.FromMinutes(_labor["minute_tick"]));
        }

        public void LaborPowerTick()
        {
            _log.Info("Labor tick in progress.");
            var connections = GameConnectionTable.Instance.GetConnections();
            foreach (var connection in connections)
            {
                foreach (var character in connection.Characters)
                {
                    var payment = connection.Payment.Method;
                    short up_limit = 0;
                    short lp_change = 0;
                    if (payment == PaymentMethodType.None) {
                        if (!character.Value.IsOnline)
                        {
                            if (_labor["fremium_offline_tick"] == 0)
                                continue;
                        }
                        up_limit = _labor["fremium_labor_maximum"];
                        lp_change = _labor["fremium_labor_change"];
                    }
                    else if (payment == PaymentMethodType.Premium)
                    {
                        if (!character.Value.IsOnline)
                        {
                            if (_labor["premium_offline_tick"] == 0)
                                continue;
                        }
                        up_limit = _labor["premium_labor_maximum"];
                        lp_change = _labor["premium_labor_change"];
                    }

                    if (character.Value.LaborPower >= up_limit)
                    {
                        _log.Info("No need to increase Labor Points for {0} as they reached the limit {1}", character.Value.Name, up_limit);
                        continue;
                    }
					
                    var change = (short)(up_limit - character.Value.LaborPower);
                    if (change >= lp_change)
                    {
                        _log.Info("Added {0} Labor Points for {1}", lp_change, character.Value.Name);
                        character.Value.LaborPowerModified = DateTime.Now;
                        character.Value.ChangeLabor(lp_change, 0);
                    }
                    else if (change != 0)
                    {
                        _log.Info("Added {0} Labor Points for {1}", change, character.Value.Name);
                        character.Value.LaborPowerModified = DateTime.Now;
                        character.Value.ChangeLabor(change, 0);
                    }
                }
            }
        }

    }
}
