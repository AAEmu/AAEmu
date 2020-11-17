using System.Collections.Generic;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using Newtonsoft.Json;
using NLog;
using System;
using System.IO;
using DotNet.Config;

namespace AAEmu.Game.Core.Managers
{
    public class ConfigurationManager : Singleton<ConfigurationManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<string, string> _configurations;

        public void Load()
        {
            _log.Info("Loading ConfigurationManager...");

            try
            {
                _configurations = AppSettings.Retrieve(@"Configs\server.properties");
            }
            catch (Exception e ){
                _log.Error(e.Message);
            }           
            
        }

        public string GetConfiguration(string configName)
        {
            if (configName == "")
            {
                throw new Exception("ConfigurationManager - No string received");
            }
            if (_configurations.ContainsKey(configName)) {
                return _configurations[configName];
            }
            return "";
        }
    }
}
