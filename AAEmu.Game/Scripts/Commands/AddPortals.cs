using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class AddPortals : ICommand
{
    public string[] CommandNames { get; set; } = new string[] { "register_portal", "add_portal", "addportal" };

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(target) <name> [<x> <y> <z> <zoneid>]";
    }

    public string GetCommandHelpText()
    {
        return "Adds a portal with <name> to your teleport book.\n" +
               "If [<x> <y> <z> <zoneid>] is omitted or incomplete, your current position will be used.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        var targetPlayer = WorldManager.GetTargetOrSelf(character, args[0], out var firstArg);

        var portalName = args[firstArg + 0];
        var position = character.Transform.CloneAsSpawnPosition();
        var x = position.X;
        var y = position.Y;
        var z = position.Z;
        var zRot = position.Roll;
        var zoneId = position.ZoneId;

        if (args.Length == firstArg + 5 && float.TryParse(args[firstArg + 1], out var argX))
        {
            x = argX;
        }

        if (args.Length == firstArg + 5 && float.TryParse(args[firstArg + 2], out var argY))
        {
            y = argY;
        }

        if (args.Length == firstArg + 5 && float.TryParse(args[firstArg + 3], out var argZ))
        {
            z = argZ;
        }

        if (args.Length == firstArg + 5 && uint.TryParse(args[firstArg + 4], out var argZoneId))
        {
            zoneId = argZoneId;
        }

        // If not using the current location, set the rotation to zero
        if (args.Length == firstArg + 5)
        {
            zRot = 0;
        }

        //targetPlayer.Portals.AddPrivatePortal(x, y, z, zRot, zoneId, portalName);
        targetPlayer.Portals.AddOrUpdatePrivatePortal(x, y, z, zRot, zoneId, portalName);
        if (character.Id != targetPlayer.Id)
        {
            CommandManager.SendNormalText(this, messageOutput,
                $"added {portalName} entry to {targetPlayer.Name}'s portal book");
            targetPlayer.SendMessage($"[GM] {character.Name} has added the entry \"{portalName}\" to your portal book");
        }
        else
        {
            CommandManager.SendNormalText(this, messageOutput, $"Registered \"{portalName}\" in your portal book");
        }
    }
}
