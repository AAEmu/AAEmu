using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
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
                character.SendMessage("[Faction] " + CommandManager.CommandPrefix + "faction <nuian||haranyan||elf||firran||pirate>");
                return;
            }

            Character targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args[0], out var firstarg);

            var newFactionId = 0u;

            var factionString = args[0];
            if (factionString == "nuian")
                newFactionId = 101u;
            else if (factionString == "elf")
                newFactionId = 103u;
            else if (factionString == "haranyan")
                newFactionId = 109u;
            else if (factionString == "firran")
                newFactionId = 113u;
            else if (factionString == "pirate")
                newFactionId = 161u;
            else if (factionString == "red")
                newFactionId = 159u;
            else if (factionString == "blue")
                newFactionId = 160u;
            else
            {
                character.SendMessage("Invalid faction");
                return;
            }

            targetPlayer.SetFaction(newFactionId);
        }
    }
}
