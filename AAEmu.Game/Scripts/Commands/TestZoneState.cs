using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.Game.Scripts.Commands
{
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

                    /*
                    Connection.SendPacket(
                        new SCConflictZoneStatePacket(
                            conflict.ZoneGroupId,
                            ZoneConflictType.Trouble0,
                            conflict.NoKillMin[0] > 0
                                ? DateTime.UtcNow.AddMinutes(conflict.NoKillMin[0])
                                : DateTime.MinValue
                        )
                    );
                    */
                }
                return;
            }

            byte zonestate = 0;
            if ((args.Length > 0) && (byte.TryParse(args[0], out byte argzonestate)))
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

            if (zonegroupid == 0)
            {
                character.SendMessage("|cFFFF0000[ZoneState] Invalid zone group id|r");
                return;
            }

            var zs = (ZoneConflictType)zonestate;

            ZoneManager.Instance.GetZoneGroupById(zonegroupid).Conflict.SetState(zs);
            // Broadcast to all online clients in server
            // WorldManager.Instance.BroadcastPacketToServer(new SCConflictZoneStatePacket(zonegroupid, (ZoneConflictType)zonestate, DateTime.MinValue));
            character.SendMessage($"|cFFFFFFFF[ZoneState] |rChanged ZoneGroup: {zonegroupid} to State: {zs}");
        }
    }
}
