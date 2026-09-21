using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class KillNpcWithoutCorpseEffect : EffectTemplate
{
    public uint NpcId { get; set; }
    public float Radius { get; set; }
    public bool GiveExp { get; set; }
    public bool Vanish { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Info($"KillNpcWithoutCorpseEffect npc={NpcId}, Radius={Radius}, GiveExp={GiveExp}, Vanish={Vanish}");

        var origin = target ?? caster;
        var nearby = origin == null
            ? []
            : WorldManager.GetAround<Npc>(origin, Radius) ?? [];
        var seen = new HashSet<uint>();
        var killer = ExperiencedBy(caster, target);
        var removed = 0;

        // A radius of 0 yields no neighbours, and the search also leaves out its own origin, so the
        // NPC the skill was aimed at is considered on its own.
        if (target is Npc skillTarget &&
            TryRemove(skillTarget, skillTarget == caster, inRadius: false, isExplicitTarget: true, killer, seen))
            removed++;

        foreach (var npc in nearby)
        {
            if (TryRemove(npc, npc == caster, inRadius: true, isExplicitTarget: false, killer, seen))
                removed++;
        }

        if (caster is Npc casterNpc &&
            TryRemove(casterNpc, unitIsCaster: true, inRadius: false, isExplicitTarget: false, killer, seen))
            removed++;

        Logger.Info(
            "KillNpcWithoutCorpseEffect npc={0} vanish={1} origin={2} nearby={3} removed={4}",
            NpcId, Vanish, origin?.ObjId ?? 0, nearby.Count, removed);
    }

    /// <summary>
    /// The character credited when <c>give_exp</c> is set: whoever the removed npc was fighting, falling back
    /// to the unit the effect was applied to.
    /// </summary>
    /// <remarks>
    /// <c>kill_npc_without_corpse_effects.give_exp</c> is a row flag and was not loaded. The player is
    /// never a victim; only NPCs whose template the row names are removed.
    /// </remarks>
    private static Character ExperiencedBy(BaseUnit caster, BaseUnit target)
    {
        var npc = caster as Npc ?? target as Npc;
        return npc?.CurrentAggroTarget as Character
               ?? caster?.GetOwnerCharacter()
               ?? target?.GetOwnerCharacter();
    }

    private bool TryRemove(Npc npc, bool unitIsCaster, bool inRadius, bool isExplicitTarget,
        Character experiencedBy, HashSet<uint> seen)
    {
        if (npc == null || npc.ObjId == 0 || !seen.Add(npc.ObjId))
            return false;
        if (!KillNpcWithoutCorpseRules.IsVictim(
                NpcId, Vanish, npc.TemplateId, unitIsCaster, npc.IsDead, inRadius, isExplicitTarget))
            return false;

        RemoveEffectsAndDelete(npc, experiencedBy);
        return true;
    }

    private void RemoveEffectsAndDelete(Unit unit, Character experiencedBy)
    {
        unit.Buffs.RemoveAllEffects();
        if (unit is not Npc npc)
            return;

        // give_exp (17 rows): the npc dies for the quest without a corpse, and the player who killed it is
        // still owed its kill exp. Npc.DoDie is not on this path at all — the npc is despawned, not killed —
        // so the award is taken here, once, from the same KillExp the ordinary death branch uses.
        if (GiveExp)
            GrantKillExp(npc, experiencedBy);

        if (WorldIntegration.ZoneAuthority)
        {
            WorldIntegration.DeleteNpcMirror(npc, true);
        }
        else if (npc.Spawner != null)
        {
            npc.DoDespawn(npc);
            npc.Spawner.DespawnWithRespawn(npc);
        }
    }

    private static void GrantKillExp(Npc npc, Character experiencedBy)
    {
        if (experiencedBy == null || npc.KillExp <= 0)
            return;

        experiencedBy.AddExp(npc.KillExp, true);
        Logger.Debug("KillNpcWithoutCorpseEffect: {0} gained {1} exp for npc {2}",
            experiencedBy.Name, npc.KillExp, npc.TemplateId);
    }
}
