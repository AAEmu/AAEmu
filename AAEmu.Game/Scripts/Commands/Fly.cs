using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Scripts.Commands
{
    public class Fly : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("fly", this);
        }

        public string GetCommandLineHelp()
        {
            return "(target) <true||false>";
        }

        public string GetCommandHelpText()
        {
            return "Enables or disables fly-mode (also makes you move at hi-speed)";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[Fly] " + CommandManager.CommandPrefix + "fly (target) <true||false>");
                return;
            }

            Character targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args[0], out var firstarg);

            if (bool.TryParse(args[firstarg + 0], out var isFlying))
                targetPlayer.SendPacket(new SCUnitFlyingStateChangedPacket(targetPlayer.ObjId, isFlying));
            else
                character.SendMessage("|cFFFF0000[Fly] Throw parse bool!|r");
        }
    }
}
