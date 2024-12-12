using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Scripts.Commands;

public class Nudge : ICommand
{
    public string[] CommandNames { get; set; } = ["nudge"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(distance)";
    }

    public string GetCommandHelpText()
    {
        return "Move yourself forward by a given distance (of 5m by default).\n" +
               "Examples:\n" +
               CommandManager.CommandPrefix + CommandNames[0] + "\n" +
               CommandManager.CommandPrefix + CommandNames[0] + " 10";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var distance = 5f;
        if (args.Length > 1 && !float.TryParse(args[0], out distance))
        {
            CommandManager.SendErrorText(this, messageOutput, $"Distance parse error");
            return;
        }

        character.DisabledSetPosition = true;
        // CommandManager.SendNormalText(this, messageOutput,$"from {character.Transform}");
        character.Transform.Local.AddDistanceToFront(distance, false);
        character.Transform.FinalizeTransform();
        // CommandManager.SendNormalText(this, messageOutput,$"to {character.Transform}");
        character.SendPacket(new SCTeleportUnitPacket(TeleportReason.Gm, 0, character.Transform.World.Position.X,
            character.Transform.World.Position.Y, character.Transform.World.Position.Z,
            character.Transform.World.Rotation.Z.DegToRad()));
    }
}
