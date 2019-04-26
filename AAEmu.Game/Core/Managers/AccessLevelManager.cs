using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.IO;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class AccessLevelManager : Singleton<AccessLevelManager>
    {
        public static List<Command> CMD = AccessLevel.CMD;
        private static Logger _log = LogManager.GetCurrentClassLogger();
        
        public void Load()
        {
            Dictionary<string, int> dic = readSettings();

            _log.Info("Loading CommandAccessLevels...");

            foreach(KeyValuePair<string, int> entry in dic)
                CMD.Add(new Command{ command = entry.Key, level = entry.Value });

            _log.Info("Loaded {0} CommandAccessLevels", CMD.Count);
        }

        public static Dictionary<string, int> readSettings(){
            Dictionary<string, int> d = new Dictionary<string, int>();
            try{
                string data = File.ReadAllText("AccessLevels.json");
                d = JsonConvert.DeserializeObject<Dictionary<string, int>>(data);
            }
            catch{}
            return d;
        }
    }
}