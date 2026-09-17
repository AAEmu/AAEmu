using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// GM command to rebuild the ranking boards now, the way the hourly tick does, so a board can be looked at
/// without waiting for the next one.
/// </summary>
public class RankRefresh : ICommand
{
    public string[] CommandNames { get; set; } = ["rankrefresh", "rankings_refresh"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "";
    }

    public string GetCommandHelpText()
    {
        return "Rebuilds every ranking board now, the way the hourly tick does.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var written = RankScoreManager.Instance.Refresh(WorldManager.Instance.GetAllCharacters());
        CommandManager.SendNormalText(this, messageOutput, $"Ranking boards rebuilt: {written} line(s).");
    }
}
