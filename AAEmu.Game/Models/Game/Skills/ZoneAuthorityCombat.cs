using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// The ZoneAuthority helpers the live cast path needs.
/// </summary>
/// <remarks>
/// World computes every skill number itself: a player cast reaches <c>Skill.Use</c> from
/// <c>CSStartSkillPacket.HandleZoneAuthorityCast</c>, and a zone-driven NPC cast reaches it from
/// <c>ZWStartSkill</c> through <c>CombatRelay.RelayStartSkill</c>. The zone owns AI, movement, aggro and
/// the pacing of its own casts, and asks World for the cast rather than computing damage itself — the
/// second, superseded NPC damage path this class used to carry had no caller and is gone. What is left is
/// the HP/MP mirror World has to keep in step and the aggro actor the zone can put on a table.
/// </remarks>
public static class ZoneAuthorityCombat
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>Call from plot DamageEffect when ZoneAuthority mirrors HP to Zone.</summary>
    public static void SyncUnitPoints(uint objId, int hp, int mp) =>
        WorldIntegration.RelayUnitPointsToZone?.Invoke(objId, hp, mp);

    /// <summary>
    /// Force-heal a mirror NPC on leash (ZWClearCombat / ZWAggroRemove on the NPC itself).
    /// Do NOT heal when ClearCombat targets the player — Zone emits that while fights are still
    /// active; healing every in-battle NPC (and pushing WZUnitPoints to full) caused mid-fight resets.
    /// </summary>
    public static bool ResetMirrorNpcHp(uint objId)
    {
        var world = WorldManager.Instance.GetWorld(WorldManager.DefaultInstanceId);
        if (world == null)
            return false;

        var npc = world.GetNpc(objId) ?? WorldIntegration.FindUnitAcrossWorlds(objId) as Npc;
        if (npc == null)
        {
            // Player ClearCombat (common) — relay SC only; NPC HP restore waits for 11503 / NPC ClearCombat.
            if (world.GetCharacterByObjId(objId) != null)
                Logger.Debug("ZWClearCombat on player {0} — skip mirror NPC heal", objId);
            return false;
        }

        if (npc.MaxHp <= 0)
            return false;

        // World (or ZWKillNpc) already authored death: Hp==0 and/or IsDead. Zone still emits
        // ZWClearCombat on the corpse; healing 0→MaxHp + SCUnitPoints after SCUnitDeath left
        // zombies (death anim + full bar) that plot TargetAlive filters still accepted.
        if (npc.Hp <= 0 || npc.IsDead)
        {
            Logger.Info("ZWClearCombat npc={0} — skip leash heal (already dead hp={1})", objId, npc.Hp);
            npc.SetBattleStateFromZone(false);
            return false;
        }

        if (npc.ZoneDespawnSignaled || npc.SportFishLineDropped)
        {
            npc.SetBattleStateFromZone(false);
            npc.ClearAllAggro();
            Logger.Info(
                "ZWClearCombat npc={0} — skip leash heal (line dropped / despawn signaled)",
                objId);
            return false;
        }

        var beforeHp = npc.Hp;
        var beforeMp = npc.Mp;
        npc.Hp = npc.MaxHp;
        npc.Mp = Math.Max(npc.MaxMp, 0);
        npc.SetBattleStateFromZone(false);
        if (WorldIntegration.IsStreamedUnitForAnyClient(objId) || beforeHp < npc.MaxHp || beforeMp != npc.Mp)
        {
            WorldIntegration.BroadcastPacketToUnitViewers(
                new SCUnitPointsPacket(objId, npc.Hp, npc.Mp), objId);
            WorldIntegration.RelayUnitPointsToZone?.Invoke(objId, npc.Hp, npc.Mp);
        }
        Logger.Info("ZoneAuthority leash heal npc={0} hp {1}→{2} mp {3}→{4}",
            objId, beforeHp, npc.Hp, beforeMp, npc.Mp);
        return true;
    }

    /// <summary>
    /// Unit the dedicate can put on an NPC aggro table. Cannon/equipment slaves are not zone
    /// combat actors; their owner (or hull) is.
    /// </summary>
    public static uint ResolveZoneCombatActorBc(BaseUnit caster)
    {
        if (caster == null || caster.ObjId == 0)
            return 0;
        if (caster is Character)
            return caster.ObjId;

        var owner = caster.GetOwnerCharacter();
        if (owner?.ObjId > 0)
            return owner.ObjId;

        if (caster is Slave slave)
        {
            if (slave.Transform?.Parent?.GameObject is Slave hull && hull.ObjId != 0)
                return hull.ObjId;
            if (slave.Template?.IsABoat() == true)
                return slave.ObjId;
        }

        return caster.ObjId;
    }
}
