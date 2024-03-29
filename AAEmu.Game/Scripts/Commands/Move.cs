using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

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

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        Character targetPlayer = character;
        var firstarg = 0;
        if (args.Length > 0)
            targetPlayer = WorldManager.GetTargetOrSelf(character, args[0], out firstarg);

        var MoveToMe = false;

        if ((targetPlayer != character) && (args.Length == 2) && (args[1].ToLower() == "tome"))
        {
            MoveToMe = true;
        }

        if ((!MoveToMe) && (args.Length < firstarg + 3) && (targetPlayer == character))
        {
            character.SendMessage("[Move] " + CommandManager.CommandPrefix + "move " + GetCommandLineHelp());
            return;
        }

        if (MoveToMe)
        {
            var myX = character.Transform.World.Position.X;
            var myY = character.Transform.World.Position.Y;
            var myZ = character.Transform.World.Position.Z + 2f; // drop them slightly above you to avoid weird collision stuff
            if (targetPlayer != character)
                targetPlayer.SendMessage($"[Move] |cFFFFFFFF{character.Name}|r has called upon your presence !");
            targetPlayer.DisabledSetPosition = true;
            targetPlayer.SendPacket(new SCTeleportUnitPacket(TeleportReason.Portal, ErrorMessageType.NoErrorMessage, myX, myY, myZ, 0f));
            character.SendMessage($"[Move] Moved |cFFFFFFFF{targetPlayer.Name}|r to your location.");
            return;
        }

        if ((args.Length == firstarg) && (targetPlayer != character))
        {
            var targetX = targetPlayer.Transform.World.Position.X;
            var targetY = targetPlayer.Transform.World.Position.Y;
            var targetZ = targetPlayer.Transform.World.Position.Z + 2f; // drop me slightly above them to avoid weird collision stuff
            character.DisabledSetPosition = true;
            character.SendPacket(new SCTeleportUnitPacket(TeleportReason.Portal, ErrorMessageType.NoErrorMessage, targetX, targetY, targetZ, 0f));
            character.SendMessage($"[Move] Moved to |cFFFFFFFF{targetPlayer.Name}|r.");
            return;
        }

        if ((args.Length == firstarg + 3) && float.TryParse(args[firstarg + 0], out var newX) && float.TryParse(args[firstarg + 1], out var newY) && float.TryParse(args[firstarg + 2], out var newZ))
        {
            if (targetPlayer != character)
                targetPlayer.SendMessage($"[Move] |cFFFFFFFF{character.Name}|r has moved you do position X: {newX}, Y: {newY}, Z: {newZ}");
            targetPlayer.DisabledSetPosition = true;
            targetPlayer.SendPacket(new SCTeleportUnitPacket(TeleportReason.Portal, ErrorMessageType.NoErrorMessage, newX, newY, newZ, 0f));
            character.SendMessage($"[Move] |cFFFFFFFF{targetPlayer.Name}|r moved to X: {newX}, Y: {newY}, Z: {newZ}");
        }
        else
        {
            character.SendMessage(ChatType.System, $"[Move] Position parse error", Color.Red);
        }

    }
}
