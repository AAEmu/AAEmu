using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.Game.Scripts.Commands;

public class TestZoneState : ICommand
{
    public void OnLoad()
    {
        CommandManager.Instance.Register("zonestate", this);
    }

    public string GetCommandLineHelp()
    {
        return "<StateId> [ZoneId]";
    }

    public string GetCommandHelpText()
    {
        return "Changes a zone's state (0=Tension, 1=Danger, 2=Dispute, 3=Unrest, 4=Crisis, 5=Conflict, 6=War, 7=Peace). If ZoneId is ommited, your current zone is used.";
    }

    public void Execute(Character character, string[] args)
    {
        if (args.Length <= 0)
        {
            // List all when no args given
            foreach (var conflict in ZoneManager.Instance.GetConflicts())
            {
                var zonegroup = ZoneManager.Instance.GetZoneGroupById(conflict.ZoneGroupId);
                character.SendMessage($"|cFFFFFFFF[ZoneState] |r ZoneGroup: {zonegroup.Name} ({conflict.ZoneGroupId}) State: {conflict.CurrentZoneState}");
            }
            return;
        }

        var zonestate = ZoneConflictType.Tension;
        if ((args.Length > 0) && (ZoneConflictType.TryParse(args[0], out ZoneConflictType argzonestate)))
            zonestate = argzonestate;

        ushort zonegroupid = 0;
        if ((args.Length > 1) && (ushort.TryParse(args[1], out ushort argzonegroupid)))
            zonegroupid = argzonegroupid;

        if (zonegroupid <= 0)
        {
            var thiszone = ZoneManager.Instance.GetZoneByKey(character.Transform.ZoneId);
            if (thiszone != null)
                zonegroupid = (ushort)(thiszone.GroupId);
        }

        if (zonegroupid > 0)
        {
            var zonegroup = ZoneManager.Instance.GetZoneGroupById(zonegroupid);

            if (zonegroup.Conflict == null)
            {
                character.SendMessage($"|cFFFF0000[ZoneState] ZoneGroup: {zonegroup.Name} ({zonegroup.Id}) does not have a conflict state defined in the game database and thus cannot be changed|r");
                return;
            }
            zonegroup.Conflict.SetState(zonestate);
            character.SendMessage($"|cFFFFFFFF[ZoneState] |rChanged ZoneGroup: {zonegroupid} to State: {zonestate}");
        }
        else
        {
            character.SendMessage($"|cFFFF0000[ZoneState] Invalid zone group id {zonegroupid}|r");
        }
    }
}
