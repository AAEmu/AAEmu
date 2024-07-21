using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class AddPortals : ICommand
{
    public void OnLoad()
    {
        string[] name = { "addportal", "add_portal" };
        CommandManager.Instance.Register(name, this);
    }

    public string GetCommandLineHelp()
    {
        return "(target) <name> [<x> <y> <z> <zoneid>]";
    }

    public string GetCommandHelpText()
    {
        return "Adds a portal with <name> to your teleport book.\n" +
            "If [<x> <y> <z> <zoneid>] is ommited or incomplete, your current position will be used.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            character.SendMessage("[Portal] " + CommandManager.CommandPrefix + "add_portal (target) <name> [<x> <y> <z> <zoneid>]");
            //character.SendMessage("[Portal] *optional (will get actual position)");
            return;
        }

        var targetPlayer = WorldManager.GetTargetOrSelf(character, args[0], out var firstarg);

        var portalName = args[firstarg + 0];
        var position = character.Transform.CloneAsSpawnPosition();
        var x = position.X;
        var y = position.Y;
        var z = position.Z;
        var zRot = position.Roll;
        var zoneId = position.ZoneId;

        if ((args.Length == firstarg + 5) && (float.TryParse(args[firstarg + 1], out var argX)))
            x = argX;
        if ((args.Length == firstarg + 5) && (float.TryParse(args[firstarg + 2], out var argY)))
            y = argY;
        if ((args.Length == firstarg + 5) && (float.TryParse(args[firstarg + 3], out var argZ)))
            z = argZ;
        if ((args.Length == firstarg + 5) && (uint.TryParse(args[firstarg + 4], out var argZoneId)))
            zoneId = argZoneId;
        // If not using the current location, set the rotation to zero
        if (args.Length == firstarg + 5)
            zRot = 0;

        targetPlayer.Portals.AddPrivatePortal(x, y, z, zRot, zoneId, portalName);
        if (character.Id != targetPlayer.Id)
        {
            character.SendMessage($"[Portal] added {portalName} entry to {targetPlayer.Name}'s portal book");
            targetPlayer.SendMessage($"[GM] {character.Name} has added the entry \"{portalName}\" to your portal book");
        }
        else
        {
            character.SendMessage($"[Portal] Registered \"{portalName}\" in your portal book");
        }
    }
}
