using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class Move : ICommand
{
    public string[] CommandNames { get; set; } = ["move"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
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
               CommandManager.CommandPrefix + CommandNames[0] + " 21000 12500 200\n" +
               CommandManager.CommandPrefix + CommandNames[0] + " TargetPlayer 21000 12500 200\n" +
               CommandManager.CommandPrefix + CommandNames[0] + " TargetPlayer ToMe\n" +
               CommandManager.CommandPrefix + CommandNames[0] + " TargetPlayer";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var targetPlayer = character;
        var firstArg = 0;
        if (args.Length > 0)
        {
            targetPlayer = WorldManager.GetTargetOrSelf(character, args[0], out firstArg);
        }

        var moveToMe = targetPlayer != character && args.Length == 2 && args[1].Equals("tome", System.StringComparison.CurrentCultureIgnoreCase);

        if (!moveToMe && args.Length < firstArg + 3 && targetPlayer == character)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        if (moveToMe)
        {
            var myX = character.Transform.World.Position.X;
            var myY = character.Transform.World.Position.Y;
            var myZ = character.Transform.World.Position.Z +
                      2f; // drop them slightly above you to avoid weird collision stuff
            if (targetPlayer != character)
            {
                targetPlayer.SendMessage($"[GM] |cFFFFFFFF{character.Name}|r has called upon your presence !");
            }

            targetPlayer.DisabledSetPosition = true;
            targetPlayer.SendPacket(new SCTeleportUnitPacket(0, 0, myX, myY, myZ, 0f));
            CommandManager.SendNormalText(this, messageOutput,
                $"Moved |cFFFFFFFF{targetPlayer.Name}|r to your location.");
            return;
        }

        if (args.Length == firstArg && targetPlayer != character)
        {
            var targetX = targetPlayer.Transform.World.Position.X;
            var targetY = targetPlayer.Transform.World.Position.Y;
            var targetZ =
                targetPlayer.Transform.World.Position.Z +
                2f; // drop me slightly above them to avoid weird collision stuff
            character.DisabledSetPosition = true;
            character.SendPacket(new SCTeleportUnitPacket(0, 0, targetX, targetY, targetZ, 0f));
            CommandManager.SendNormalText(this, messageOutput, $"Moved to |cFFFFFFFF{targetPlayer.Name}|r.");
            return;
        }

        if (args.Length == firstArg + 3 && float.TryParse(args[firstArg + 0], out var newX) &&
            float.TryParse(args[firstArg + 1], out var newY) && float.TryParse(args[firstArg + 2], out var newZ))
        {
            if (targetPlayer != character)
            {
                targetPlayer.SendMessage(
                    $"[GM] |cFFFFFFFF{character.Name}|r has moved you do position X: {newX}, Y: {newY}, Z: {newZ}");
            }

            targetPlayer.DisabledSetPosition = true;
            targetPlayer.SendPacket(new SCTeleportUnitPacket(0, 0, newX, newY, newZ, 0f));
            CommandManager.SendNormalText(this, messageOutput,
                $"|cFFFFFFFF{targetPlayer.Name}|r moved to X: {newX}, Y: {newY}, Z: {newZ}");
        }
        else
        {
            CommandManager.SendErrorText(this, messageOutput, $"Position parse error");
        }
    }
}
