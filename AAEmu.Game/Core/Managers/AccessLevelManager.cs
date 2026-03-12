using AAEmu.Commons.Utils;
using AAEmu.Game.Models;
using Microsoft.Extensions.Options;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class AccessLevelManager(IOptions<AppConfiguration> options) : Singleton<AccessLevelManager>, IAccessLevelManager
{
    private readonly List<Command> CMD = [];
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public void Load()
    {
        Logger.Info("Loading CommandAccessLevels...");

        foreach (var (cmdName, cmdLevel) in options.Value.AccessLevel)
            CMD.Add(new Command { CommandName = cmdName, CommandLevel = cmdLevel });

        Logger.Info($"Loaded {CMD.Count} CommandAccessLevels");
    }

    public int GetLevel(string commandStr)
    {
        var result = CMD.Find(o => o.CommandName == commandStr);
        if (result != null)
            return result.CommandLevel;

        return 100;
    }
}

public class Command
{
    public string CommandName { get; set; }
    public int CommandLevel { get; set; }
}
