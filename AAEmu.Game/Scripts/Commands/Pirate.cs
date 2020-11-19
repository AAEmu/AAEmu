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
            CommandManager.Instance.Register("pirate", this);
        }

        public string GetCommandLineHelp()
        {
            return "(target) <true||false>";
        }

        public string GetCommandHelpText()
        {
            return "Makes target pirate/revert back to original faction";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[Pirate] " + CommandManager.CommandPrefix + "pirate (target) <true||false>");
                return;
            }

            var targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args[0], out var firstarg);

            if (bool.TryParse(args[firstarg + 0], out var isPirate))
                targetPlayer.SetPirate(isPirate);
            else
                character.SendMessage("|cFFFF0000[Pirate] Throw parse bool!|r");
        }
    }
}
