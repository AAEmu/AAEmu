using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Scripts.Commands
{
    public class Pirate: ICommand
    {
        public void OnLoad()
        {
            string[] names = { "setfaction", "set_faction" };
            CommandManager.Instance.Register(names, this);
        }

        public string GetCommandLineHelp()
        {
            return "<nuian||haranyan||elf||firran||pirate>";
        }

        public string GetCommandHelpText()
        {
            return "Sets your faction";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage($"[Faction] {CommandManager.CommandPrefix}faction <nuian||haranyan||elf||firran||pirate||friendly>");
                return;
            }

            uint newFactionId = 0;
            var factionString = args[0];
            if (factionString == "nuian")
                newFactionId = 101;
            else if (factionString == "elf")
                newFactionId = 103;
            else if (factionString == "haranyan")
                newFactionId = 109;
            else if (factionString == "firran")
                newFactionId = 113;
            else if (factionString == "pirate")
                newFactionId = 161;
            else if (factionString == "red")
                newFactionId = 159;
            else if (factionString == "blue")
                newFactionId = 160;
            else if (factionString == "friendly")
                newFactionId = 1;
            else
            {
                character.SendMessage("Invalid faction");
                return;
            }

            character.SetFaction(newFactionId);
        }
    }
}
