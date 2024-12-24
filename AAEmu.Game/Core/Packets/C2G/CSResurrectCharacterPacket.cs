using System.Linq;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSResurrectCharacterPacket : GamePacket
{
    public CSResurrectCharacterPacket() : base(CSOffsets.CSResurrectCharacterPacket, 1)
    {
    }

    public override void Read(PacketStream stream)
    {
        var inPlace = stream.ReadBoolean();

        Logger.Debug("ResurrectCharacter, InPlace: {0}", inPlace);

        var portal = new Portal();

        // поищем сначала "UnitId": 502, "Title": "Temple Priestess",
        // Inside dungeons or other instances, just respawn at the nearest Priestess
        if (Connection.ActiveChar.Transform.WorldId != WorldManager.DefaultWorldId)
        {
            var npcs = WorldManager.Instance.GetAllNpcsFromWorld(Connection.ActiveChar.Transform.WorldId);
            foreach (var npc in npcs.Where(npc => npc.TemplateId == 502))
            {
                portal.WorldId = Connection.ActiveChar.Transform.WorldId;
                portal.ZoneId = npc.Transform.ZoneId;
                portal.X = npc.Transform.World.Position.X + Rand.Next(1, 3);
                portal.Y = npc.Transform.World.Position.Y + Rand.Next(1, 3);
                portal.Z = npc.Transform.World.Position.Z;
                portal.ZRot = npc.Transform.World.Rotation.Z;
                portal.Yaw = npc.Transform.World.Rotation.Z;
            }
        }
        else
        {
            // Check if the current zone is at War and if it has special respawn areas for factions
            var usePortalId = 0u;
            var currentZone = ZoneManager.Instance.GetZoneByKey(Connection.ActiveChar.Transform.ZoneId);
            if (currentZone != null)
            {
                var conflictData = ZoneManager.Instance.GetConflicts()?.FirstOrDefault(c => c.ZoneGroupId == currentZone.GroupId);
                if (conflictData?.CurrentZoneState == ZoneConflictType.War)
                {
                    switch (Connection.ActiveChar.Faction.MotherId)
                    {
                        case FactionsEnum.NuiaAlliance:
                            usePortalId = conflictData.NuiaReturnPointId;
                            break;
                        case FactionsEnum.HaranyaAlliance:
                            usePortalId = conflictData.HariharaReturnPointId;
                            break;
                    }
                }
            }

            // Try to get a faction specific respawn
            if (usePortalId > 0)
            {
                portal = PortalManager.Instance.GetRespawnById(usePortalId);
            }
            
            // Find the closest return portal (in the world) for the player if none has been found yet
            if ((usePortalId == 0) || (portal == null))
            {
                portal = PortalManager.Instance.GetClosestReturnPortal(Connection.ActiveChar);
            }
        }

        if (inPlace)
        {
            Connection.ActiveChar.Hp = (int)(Connection.ActiveChar.MaxHp * (Connection.ActiveChar.ResurrectHpPercent / 100.0f));
            Connection.ActiveChar.Mp = (int)(Connection.ActiveChar.MaxMp * (Connection.ActiveChar.ResurrectMpPercent / 100.0f));
            Connection.ActiveChar.ResurrectHpPercent = 1;
            Connection.ActiveChar.ResurrectMpPercent = 1;
            Connection.ActiveChar.PostUpdateCurrentHp(Connection.ActiveChar, 0, Connection.ActiveChar.Hp, KillReason.Unknown);
        }
        else
        {
            Connection.ActiveChar.Hp = (int)(Connection.ActiveChar.MaxHp * 0.1);
            Connection.ActiveChar.Mp = (int)(Connection.ActiveChar.MaxMp * 0.1);
            Connection.ActiveChar.PostUpdateCurrentHp(Connection.ActiveChar, 0, Connection.ActiveChar.Hp, KillReason.Unknown);
        }

        if (portal.X != 0)
        {
            Connection.ActiveChar.BroadcastPacket(
                new SCCharacterResurrectedPacket(
                    Connection.ActiveChar.ObjId,
                    portal.X,
                    portal.Y,
                    portal.Z,
                    portal.ZRot
                ),
                true
            );
        }
        else
        {
            Connection.ActiveChar.BroadcastPacket(
                new SCCharacterResurrectedPacket(
                    Connection.ActiveChar.ObjId,
                    Connection.ActiveChar.Transform.World.Position.X,
                    Connection.ActiveChar.Transform.World.Position.Y,
                    Connection.ActiveChar.Transform.World.Position.Z,
                    0
                ),
                true
            );
        }

        Connection.ActiveChar.BroadcastPacket(
            new SCUnitPointsPacket(
                Connection.ActiveChar.ObjId,
                Connection.ActiveChar.Hp,
                Connection.ActiveChar.Mp
            ),
            true
        );
        Connection.ActiveChar.IsUnderWater = false;
        //Connection.ActiveChar.StartRegen();
        Connection.ActiveChar.Breath = Connection.ActiveChar.LungCapacity;
    }
}
