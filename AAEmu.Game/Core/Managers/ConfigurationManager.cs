using System.Collections.Generic;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Utils.DB;
using NLog;
using System;
using System.IO;
using ConfigurationInstance = AAEmu.Game.Models.Game.Configurations;

namespace AAEmu.Game.Core.Managers
{
    public class ConfigurationManager : Singleton<ConfigurationManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<String, String> _configurations;

        public void Load()
        {
            _log.Info("Loading ConfigurationManager...");

            #region FileManager
            _configurations = new Dictionary<String, String>();
            var pathFile = $"{FileManager.AppPath}Data/Configurations.json";
            var contents = FileManager.GetFileContents(pathFile);
            if (string.IsNullOrWhiteSpace(contents))
                throw new IOException($"File {pathFile} doesn't exists or is empty.");

            if (JsonHelper.TryDeserializeObject(contents, out List<ConfigurationInstance> configurations, out _))
            {
                foreach (var config in configurations)
                {
                    _configurations.Add(config.Key,config.Value);
                }
            }
            else
                throw new Exception($"ConfigurationManager: Parse {pathFile} file");
            #endregion
        }

        public string GetConfiguration(string configName)
        {
            if (configName == "") return "";
            if (_configurations.ContainsKey(configName)) {
                return _configurations[configName];
            }
            else
            {
                return "";
            }
        }
    }
}
