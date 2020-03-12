using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Scripts.Commands
{
    public class SoloParty : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "soloparty", "solo_party" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "";
        }

        public string GetCommandHelpText()
        {
            return "Creates a party with just yourself in it. This can be usefull to use with \"" + CommandManager.CommandPrefix + "teleport .\" command.";
        }

        public void Execute(Character character, string[] args)
        {
            var currentTeam = TeamManager.Instance.GetActiveTeamByUnit(character.Id);
            if (currentTeam != null)
                character.SendMessage("|cFFFFFF00[SoloParty] You are already in a party !|r");
            else
                TeamManager.Instance.CreateSoloTeam(character,true);
        }
    }
}
