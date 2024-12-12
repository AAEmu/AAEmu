using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class SoloParty : ICommand
{
    public string[] CommandNames { get; set; } = ["soloparty", "solo_party"];

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
        return "Creates a party with just yourself in it. This can be useful to use with \"" +
               CommandManager.CommandPrefix + "teleport .\" command.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var currentTeam = TeamManager.Instance.GetActiveTeamByUnit(character.Id);
        if (currentTeam != null)
        {
            character.SendMessage("|cFFFFFF00[SoloParty] You are already in a party !|r");
        }
        else
        {
            TeamManager.Instance.CreateSoloTeam(character, true);
        }
    }
}
