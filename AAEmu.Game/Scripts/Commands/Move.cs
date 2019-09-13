using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.Game.Scripts.Commands
{
    public class Move : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("move", this);
        }

        public string GetCommandLineHelp()
        {
            return "<x> <y> <z>";
        }

        public string GetCommandHelpText()
        {
            return "Move to target position at <x> <y> <z>";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length < 2)
            {
                character.SendMessage("[Move] /move <x> <y> <z>");
                return;
            }

            var newX = float.Parse(args[0]);
            var newY = float.Parse(args[1]);
            var newZ = float.Parse(args[2]);

            character.DisabledSetPosition = true;
            character.SendPacket(new SCTeleportUnitPacket(0, 0, newX, newY, newZ, 0f));
            character.SendMessage("[Move] X: {0}, Y: {1}, Z: {2}", newX, newY, newZ);
        }
    }
}
