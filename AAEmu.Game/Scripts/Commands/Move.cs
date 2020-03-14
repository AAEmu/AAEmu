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
            return "(target) [<x> <y> <z>||ToMe]";
        }

        public string GetCommandHelpText()
        {
            return "Move yourself or (target) player to position <x> <y> <z>.\n" +
                "You can also use \"ToMe\" instead of coordinates to teleport (target) to your location.\n" +
                "Or you can only specify a target player to move to that player.\n" +
                "Examples:\n" +
                CommandManager.CommandPrefix + "move 21000 12500 200\n" +
                CommandManager.CommandPrefix + "move TargetPlayer 21000 12500 200\n" +
                CommandManager.CommandPrefix + "move TargetPlayer ToMe\n" +
                CommandManager.CommandPrefix + "move TargetPlayer";
        }

        public void Execute(Character character, string[] args)
        {
            Character targetPlayer = character;
            var firstarg = 0;
            if (args.Length > 0)
                targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args[0], out firstarg);

            var MoveToMe = false;

            if ((targetPlayer != character) && (args.Length == 2) && (args[1].ToLower() == "tome"))
            {
                MoveToMe = true;
            }

            if ((!MoveToMe) && (args.Length < firstarg+3) && (targetPlayer == character))
            {
                character.SendMessage("[Move] " + CommandManager.CommandPrefix + "move " + GetCommandLineHelp());
                return;
            }

            if (MoveToMe)
            {
                var myX = character.Position.X;
                var myY = character.Position.Y;
                var myZ = character.Position.Z + 2f; // drop them slightly above you to avoid weird collision stuff
                if (targetPlayer != character)
                    targetPlayer.SendMessage("[Move] GM |cFFFFFFFF{0}|r has called upon your presence !", character.Name);
                targetPlayer.DisabledSetPosition = true;
                targetPlayer.SendPacket(new SCTeleportUnitPacket(0, 0, myX, myY, myZ, 0f));
                character.SendMessage("[Move] Moved |cFFFFFFFF{0}|r to your location.", targetPlayer.Name);
                return;
            }
            
            if ((args.Length == firstarg) && (targetPlayer != character))
            {
                var targetX = targetPlayer.Position.X;
                var targetY = targetPlayer.Position.Y;
                var targetZ = targetPlayer.Position.Z + 2f; // drop me slightly above them to avoid weird collision stuff
                character.DisabledSetPosition = true;
                character.SendPacket(new SCTeleportUnitPacket(0, 0, targetX, targetY, targetZ, 0f));
                character.SendMessage("[Move] Moved to |cFFFFFFFF{0}|r.", targetPlayer.Name);
                return;
            }

            if ((args.Length == firstarg + 3) && float.TryParse(args[firstarg + 0], out var newX) && float.TryParse(args[firstarg + 1], out var newY) && float.TryParse(args[firstarg + 2], out var newZ))
            {
                if (targetPlayer != character)
                    targetPlayer.SendMessage("[Move] GM |cFFFFFFFF{0}|r has moved you do position X: {1}, Y: {2}, Z: {3}", character.Name, newX, newY, newZ);
                targetPlayer.DisabledSetPosition = true;
                targetPlayer.SendPacket(new SCTeleportUnitPacket(0, 0, newX, newY, newZ, 0f));
                character.SendMessage("[Move] |cFFFFFFFF{0}|r moved to X: {1}, Y: {2}, Z: {3}", targetPlayer.Name, newX, newY, newZ);
            }
            else
            {
                character.SendMessage("|cFFFF0000[Move] Position parse error|r");
            }

        }
    }
}
