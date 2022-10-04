using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

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
            return $"Creates a party with just yourself in it. This can be useful to use with \"{CommandManager.CommandPrefix}teleport .\" command.";
        }

        public void Execute(Character character, string[] args)
        {
            if (TeamManager.Instance.GetActiveTeamByUnit(character.Id) != null)
            {
                character.SendMessage("|cFFFFFF00[SoloParty] You are already in a party!|r");
                return;
            }

            TeamManager.Instance.CreateSoloTeam(character,true);
        }
    }
}
