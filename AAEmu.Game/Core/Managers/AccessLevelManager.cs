using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.Models;

using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class AccessLevelManager : Singleton<AccessLevelManager>
    {
        private List<Command> CMD = new List<Command>();
        private Logger _log = LogManager.GetCurrentClassLogger();

        public void Load()
        {
            _log.Info("Loading CommandAccessLevels...");

            foreach (var (cmdName, cmdLevel) in AppConfiguration.Instance.AccessLevel)
            {
                CMD.Add(new Command { CommandName = cmdName, CommandLevel = cmdLevel });
            }

            _log.Info($"Loaded {CMD.Count} CommandAccessLevels");
        }

        public int GetLevel(string commandStr)
        {
            var result = CMD.Find(o => o.CommandName == commandStr);
            if (result != null)
            {
                return result.CommandLevel;
            }

            return 100;
        }
    }

    public class Command
    {
        public string CommandName { get; set; }
        public int CommandLevel { get; set; }
    }
}
