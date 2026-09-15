using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using NLog;

namespace AAEmu.Game.Models.Game.Units;

/// <summary>
/// The combat_buffs entries a unit has picked up from its own buffs, indexed by hit type.
/// </summary>
/// <remarks>
/// combat_buffs is keyed by req_buff_id: an entry becomes live for a unit when that buff lands on it
/// (Buffs.AddBuff), so <c>owner</c> is always the combatant carrying the required buff. Each entry is
/// indexed once per hit type its hit_type_bits mask sets (<see cref="CombatBuffHitRules"/>), which is
/// what keeps add and remove symmetric.
/// </remarks>
public class CombatBuffs(BaseUnit owner)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly Dictionary<SkillHitType, List<CombatBuffTemplate>> _cbuffsByHitType = [];

    public void AddCombatBuffs(uint buffId)
    {
        foreach (var buffToAdd in SkillManager.Instance.GetCombatBuffs(buffId))
        {
            foreach (var hitType in buffToAdd.HitTypes)
            {
                if (!_cbuffsByHitType.TryGetValue(hitType, out var value))
                {
                    value = [];
                    _cbuffsByHitType.Add(hitType, value);
                }

                // A buff can be re-applied while its entry is still live; keeping one copy per hit type
                // stops RemoveCombatBuff from leaving a second one behind.
                if (!value.Contains(buffToAdd))
                    value.Add(buffToAdd);
            }
        }
    }

    public void RemoveCombatBuff(uint buffId)
    {
        foreach (var buffToRemove in SkillManager.Instance.GetCombatBuffs(buffId))
        {
            foreach (var hitType in buffToRemove.HitTypes)
            {
                if (_cbuffsByHitType.TryGetValue(hitType, out var value))
                    value.RemoveAll(template => ReferenceEquals(template, buffToRemove));
            }
        }
    }

    /// <summary>
    /// Applies this unit's combat buffs that react to a hit whose outcome is <paramref name="type"/>.
    /// The callers ask both combatants of the hit in turn, and each entry fires only in the list of the
    /// unit <c>buff_to_source</c> points at.
    /// </summary>
    /// <param name="attacker">The unit that landed the hit.</param>
    /// <param name="receiver">The unit that took the hit; null when it is not a Unit, e.g. a house.</param>
    /// <param name="type">The hit outcome the caller resolved.</param>
    /// <param name="isHeal">True for a critical heal; the is_heal_spell rows only fire from those.</param>
    /// <param name="skill">The skill that landed, for hit_skill_id and hit_skill_tag_id. Null on effect
    /// ticks that cannot be attributed to a skill.</param>
    public void TriggerCombatBuffs(BaseUnit attacker, BaseUnit receiver, SkillHitType type, bool isHeal,
        Skill skill = null)
    {
        if (owner is not Unit unit)
            return;
        if (!_cbuffsByHitType.TryGetValue(type, out var combatBuffs))
            return;

        var landedSkillId = skill?.Template?.Id ?? 0;
        var landedSkillTags = landedSkillId != 0 ? SkillManager.Instance.GetSkillTags(landedSkillId) : [];

        foreach (var cb in combatBuffs)
        {
            if (cb.IsHealSpell != isHeal)
                continue;

            var ownerIsAttacker = ReferenceEquals(owner, attacker);
            if (!CombatBuffHitRules.FiresForOwner(cb.BuffToSource, ownerIsAttacker))
                continue;

            if (!CombatBuffHitRules.MatchesHitSkill(cb.HitSkillId, cb.HitSkillTagId, landedSkillId, landedSkillTags))
                continue;

            var other = ownerIsAttacker ? receiver as Unit : attacker as Unit;
            var target = CombatBuffHitRules.BuffsOwner(cb.ReverseTargetOn) ? unit : other;
            // A reversed row needs the other combatant to exist; a hit on a house has none.
            if (target == null)
                continue;

            var source = CombatBuffHitRules.CasterIsAttacker(cb.BuffFromSource, ReferenceEquals(target, attacker))
                ? attacker as Unit ?? target
                : target;

            var buffTemplate = SkillManager.Instance.GetBuffTemplate(cb.BuffId);
            if (buffTemplate == null)
            {
                // combat_buffs 153 carries buff_id 0: it only grants combat_resource_id 15, which this
                // path does not implement. The loader warns about that row once per start.
                Logger.Debug("combat_buffs {0}: no buff template for buff_id {1} — skipped", cb.Id, cb.BuffId);
                continue;
            }

            Logger.Debug("combat_buffs {0} (req buff {1}, bits {2}, {3}): {4} -> {5}{6}", cb.Id, cb.ReqBuffId,
                cb.HitTypeBits, type, source.ObjId, target.ObjId, cb.ReverseTargetOn ? " reversed" : "");

            // Immunity is tested on the unit the buff lands on, with the caster and the skill that landed
            // behind it (BuffImmunityRules reads both for immune_except_* gates).
            if (target.Buffs.CheckBuffImmune(buffTemplate, source, skill))
                continue;

            target.Buffs.AddBuff(new Buff(target, source, new SkillCasterUnit(source.ObjId), buffTemplate, null,
                DateTime.UtcNow));
        }
    }
}
