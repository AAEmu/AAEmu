using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class TestZoneState : ICommand
{
    public string[] CommandNames { get; set; } = ["zonestate", "zone_state"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<StateId> [ZoneId]";
    }

    public string GetCommandHelpText()
    {
        return
            "Changes a zone's state (0=Tension, 1=Danger, 2=Dispute, 3=Unrest, 4=Crisis, 5=Conflict, 6=War, 7=Peace). If ZoneId is ommited, your current zone is used.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length <= 0)
        {
            // List all when no args given
            foreach (var conflict in ZoneManager.Instance.GetConflicts())
            {
                var zonegroup = ZoneManager.Instance.GetZoneGroupById(conflict.ZoneGroupId);
                CommandManager.SendNormalText(this, messageOutput,
                    $"ZoneGroup: {zonegroup.Name} ({conflict.ZoneGroupId}) State: {conflict.CurrentZoneState}");
            }

            return;
        }

        var zonestate = ZoneConflictType.Tension;
        if (args.Length > 0 && Enum.TryParse(args[0], out ZoneConflictType argzonestate))
        {
            zonestate = argzonestate;
        }

        ushort zonegroupid = 0;
        if (args.Length > 1 && ushort.TryParse(args[1], out var argzonegroupid))
        {
            zonegroupid = argzonegroupid;
        }

        if (zonegroupid <= 0)
        {
            var thiszone = ZoneManager.Instance.GetZoneByKey(character.Transform.ZoneId);
            if (thiszone != null)
            {
                zonegroupid = (ushort)thiszone.GroupId;
            }
        }

        if (zonegroupid > 0)
        {
            var zonegroup = ZoneManager.Instance.GetZoneGroupById(zonegroupid);

            if (zonegroup.Conflict == null)
            {
                CommandManager.SendErrorText(this, messageOutput,
                    $"ZoneGroup: {zonegroup.Name} ({zonegroup.Id}) does not have a conflict state defined in the game database and thus cannot be changed");
                return;
            }

            zonegroup.Conflict.SetState(zonestate);
            CommandManager.SendNormalText(this, messageOutput,
                $"Changed ZoneGroup: {zonegroupid} to State: {zonestate}");
        }
        else
        {
            CommandManager.SendErrorText(this, messageOutput, $"Invalid zone group id {zonegroupid}|r");
        }
    }
}
