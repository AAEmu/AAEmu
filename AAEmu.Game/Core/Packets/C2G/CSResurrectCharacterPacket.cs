using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSResurrectCharacterPacket() : GamePacket(CSOffsets.CSResurrectCharacterPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var inPlace = stream.ReadBoolean();

        Logger.Debug("ResurrectCharacter, InPlace: {0}", inPlace);

        var portal = new Portal();

        // поищем сначала "UnitId": 502, "Title": "Temple Priestess",
        // Inside dungeons or other instances, just respawn at the nearest Priestess
        if (Connection.ActiveChar.Transform.InstanceId != WorldManager.DefaultInstanceId)
        {
            var npcs = Connection.ActiveChar.ParentWorld.GetAllNpcs();
            foreach (var npc in npcs.Where(npc => npc.TemplateId == 502))
            {
                portal.WorldId = Connection.ActiveChar.Transform.WorldId;
                portal.ZoneId = npc.Transform.ZoneId;
                portal.X = npc.Transform.World.Position.X + Random.Shared.Next(1, 3);
                portal.Y = npc.Transform.World.Position.Y + Random.Shared.Next(1, 3);
                portal.Z = npc.Transform.World.Position.Z;
                portal.ZRot = npc.Transform.World.Rotation.Z;
                portal.Yaw = npc.Transform.World.Rotation.Z;
                break;
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
            if (usePortalId == 0 || portal == null)
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

        // ── PvP-Honor system: route death-debuffs by death context ─────────
        if (inPlace)
        {
            // Player-resurrected → no debuffs at all
            Connection.ActiveChar.DiedInPvpWarZone = false;
            Connection.ActiveChar.DiedInPvp = false;
        }
        else if (Connection.ActiveChar.DiedInPvpWarZone)
        {
            // War-zone PvP death → Leech (4424) + Respawn-CD (2385, 5min)
            Connection.ActiveChar.DiedInPvpWarZone = false;
            Connection.ActiveChar.DiedInPvp = false;
            var casterObj = new SkillCasterUnit(Connection.ActiveChar.ObjId);

            var leechTemplate = SkillManager.Instance.GetBuffTemplate(4424);
            if (leechTemplate != null)
                Connection.ActiveChar.Buffs.AddBuff(new Buff(Connection.ActiveChar, Connection.ActiveChar, casterObj, leechTemplate, null, DateTime.UtcNow));

            var cdTemplate = SkillManager.Instance.GetBuffTemplate(2385);
            if (cdTemplate != null)
            {
                var cdBuff = new Buff(Connection.ActiveChar, Connection.ActiveChar, casterObj, cdTemplate, null, DateTime.UtcNow);
                Connection.ActiveChar.Buffs.AddBuff(cdBuff, forcedDuration: 300_000);
            }
        }
        else if (Connection.ActiveChar.DiedInPvp)
        {
            // Conflict-zone PvP death → Respawn-CD only (no Weakened Body)
            Connection.ActiveChar.DiedInPvp = false;
            var casterObj = new SkillCasterUnit(Connection.ActiveChar.ObjId);

            var cdTemplate = SkillManager.Instance.GetBuffTemplate(2385);
            if (cdTemplate != null)
            {
                var cdBuff = new Buff(Connection.ActiveChar, Connection.ActiveChar, casterObj, cdTemplate, null, DateTime.UtcNow);
                Connection.ActiveChar.Buffs.AddBuff(cdBuff, forcedDuration: 300_000);
            }
        }
        else
        {
            // PvE death → Weakened Body (1128) + Respawn-CD (2385, 5min)
            var casterObj = new SkillCasterUnit(Connection.ActiveChar.ObjId);

            var weakenedTemplate = SkillManager.Instance.GetBuffTemplate(1128);
            if (weakenedTemplate != null)
                Connection.ActiveChar.Buffs.AddBuff(new Buff(Connection.ActiveChar, Connection.ActiveChar, casterObj, weakenedTemplate, null, DateTime.UtcNow));

            var cdTemplate = SkillManager.Instance.GetBuffTemplate(2385);
            if (cdTemplate != null)
            {
                var cdBuff = new Buff(Connection.ActiveChar, Connection.ActiveChar, casterObj, cdTemplate, null, DateTime.UtcNow);
                Connection.ActiveChar.Buffs.AddBuff(cdBuff, forcedDuration: 300_000);
            }
        }

        Connection.ActiveChar.IsUnderWater = false;
        //Connection.ActiveChar.StartRegen();
        Connection.ActiveChar.Breath = Connection.ActiveChar.LungCapacity;
    }
}
