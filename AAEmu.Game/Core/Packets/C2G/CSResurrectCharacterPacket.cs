using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Models.StaticValues;

using WorldIntegration = AAEmu.Game.WorldIntegration;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSResurrectCharacterPacket() : GamePacket(CSOffsets.CSResurrectCharacterPacket, 1)
{
    /// <summary>Duration of the post-revive Respawn-Cooldown debuff in milliseconds (5 min).</summary>
    private const int RespawnCooldownDurationMs = 300_000;

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
                var conflictData = ZoneManager.Instance.GetConflicts().FirstOrDefault(c => c.ZoneGroupId == currentZone.GroupId);
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

        var character = Connection.ActiveChar;
        var oldHp = character.Hp;

        // Resolve spawn point before restoring vitals. Historically we only told the *client*
        // SCCharacterResurrected coordinates and left World Transform at the corpse. With Zone
        // authority, NPCs still had an active unit at the death scene — the moment we set Hp > 0 they
        // hit again, pinned Hp to 0, and GamePing re-emitted zeros while the player stood at temple
        // (IsDead blocks RegenTick). Same-level res must move the server unit first.
        float rx, ry, rz, rrot;
        if (portal != null && (portal.X != 0f || portal.Y != 0f))
        {
            rx = portal.X;
            ry = portal.Y;
            rz = portal.Z;
            rrot = portal.ZRot;
        }
        else
        {
            var p = character.Transform.World.Position;
            rx = p.X;
            ry = p.Y;
            rz = p.Z;
            rrot = character.Transform.World.Rotation.Z;
        }

        // Same-instance temple resurrection must update the authoritative transform here.
        character.DisabledSetPosition = false;
        character.SetPosition(rx, ry, rz, 0f, 0f, rrot);
        character.Transform.FinalizeTransform();

        character.IsInBattle = false;
        character.CurrentTarget = null;
        character.IsUnderWater = false;
        character.Breath = character.LungCapacity;

        // Apply the final vital floor after revival debuffs so conversion effects cannot leave the
        // character dead immediately after resurrection.
        int newHp;
        int newMp;
        if (inPlace)
        {
            var hpPct = character.ResurrectHpPercent;
            var mpPct = character.ResurrectMpPercent;
            if (hpPct == 0)
                hpPct = 1;
            if (mpPct == 0)
                mpPct = 1;
            newHp = Math.Max(1, (int)Math.Ceiling(character.MaxHp * (hpPct / 100.0)));
            newMp = Math.Max(0, (int)Math.Ceiling(character.MaxMp * (mpPct / 100.0)));
            character.ResurrectHpPercent = 1;
            character.ResurrectMpPercent = 1;
        }
        else
        {
            // Temple / return-point res — 10% with a hard floor of 1.
            newHp = Math.Max(1, (int)Math.Ceiling(character.MaxHp * 0.1));
            newMp = Math.Max(0, (int)Math.Ceiling(character.MaxMp * 0.1));
        }

        // Zone resurrection while World Hp is still 0 so leftover skill hits early-out.
        if (WorldIntegration.ZoneAuthority)
        {
            WorldIntegration.RelayUnitResurrectionToZone?.Invoke(
                character.ObjId, rx, ry, rz, rrot);
            WorldIntegration.RelayCombatClearedToZone?.Invoke(character.ObjId);
        }

        // Debuffs first: OnStarted conversions clamp drain to current HP (0 → no-op).
        ApplyRevivalDebuffs(character, inPlace);

        character.Hp = newHp;
        character.Mp = newMp;
        // Safety: never exit revive dead.
        if (character.Hp <= 0)
            character.Hp = Math.Max(1, newHp);

        if (WorldIntegration.ZoneAuthority)
            WorldIntegration.RelayUnitPointsToZone?.Invoke(character.ObjId, character.Hp, character.Mp);

        character.BroadcastPacket(
            new SCCharacterResurrectedPacket(character.ObjId, rx, ry, rz, rrot),
            true
        );

        character.BroadcastPacket(
            new SCUnitPointsPacket(character.ObjId, character.Hp, character.Mp),
            true
        );

        if (character.Hp > 0)
            character.PostUpdateCurrentHp(character, oldHp, character.Hp, KillReason.Unknown);

        Logger.Info(
            "ResurrectCharacter {0} inPlace={1} hp={2}/{3} mp={4}/{5} pos=({6:F1},{7:F1},{8:F1})",
            character.Name, inPlace, character.Hp, character.MaxHp, character.Mp, character.MaxMp,
            rx, ry, rz);
    }

    /// <summary>
    /// Apply post-revive debuffs based on the death context:
    ///   inPlace (player-res) → no debuffs at all
    ///   DiedInPvpWarZone     → Leech + 5 min Respawn-CD
    ///   DiedInPvp            → 5 min Respawn-CD only (no Weakened Body)
    ///   PvE death            → Weakened Body + 5 min Respawn-CD
    /// </summary>
    private static void ApplyRevivalDebuffs(Character character, bool inPlace)
    {
        if (inPlace)
        {
            // Player-resurrected (e.g. by another player's resurrect skill): no debuffs.
            character.DiedInPvpWarZone = false;
            character.DiedInPvp = false;
            return;
        }

        var casterObj = new SkillCasterUnit(character.ObjId);

        if (character.DiedInPvpWarZone)
        {
            // PvP death in War zone → Leech + Respawn-CD
            character.DiedInPvpWarZone = false;
            character.DiedInPvp = false;
            ApplyBuff(character, casterObj, (uint)BuffConstants.WarZoneLeech);
            ApplyBuff(character, casterObj, (uint)BuffConstants.RespawnCooldown, RespawnCooldownDurationMs);
        }
        else if (character.DiedInPvp)
        {
            // PvP death outside War zone → Respawn-CD only (no Weakened Body)
            character.DiedInPvp = false;
            ApplyBuff(character, casterObj, (uint)BuffConstants.RespawnCooldown, RespawnCooldownDurationMs);
        }
        else
        {
            // PvE death → Weakened Body + Respawn-CD
            ApplyBuff(character, casterObj, (uint)BuffConstants.WeakenedBody);
            ApplyBuff(character, casterObj, (uint)BuffConstants.RespawnCooldown, RespawnCooldownDurationMs);
        }
    }

    private static void ApplyBuff(Character character, SkillCasterUnit casterObj, uint buffId, int forcedDurationMs = 0)
    {
        var template = SkillManager.Instance.GetBuffTemplate(buffId);
        if (template == null)
            return;

        var buff = new Buff(character, character, casterObj, template, null, DateTime.UtcNow);
        if (forcedDurationMs > 0)
            character.Buffs.AddBuff(buff, forcedDuration: forcedDurationMs);
        else
            character.Buffs.AddBuff(buff);
    }
}
