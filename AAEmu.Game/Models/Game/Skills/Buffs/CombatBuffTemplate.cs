namespace AAEmu.Game.Models.Game.Skills.Buffs;

public class CombatBuffTemplate
{
    private SkillHitType[] _hitTypes;

    public uint Id { get; set; }
    /// <summary>
    /// <c>hit_type_bits</c> — the mask of hit types the row reacts to, one bit per hit type. See
    /// <see cref="CombatBuffHitRules"/> for the layout and the evidence for it.
    /// </summary>
    public uint HitTypeBits { get; set; }
    /// <summary>The hit types <see cref="HitTypeBits"/> sets, decoded once. Empty when the mask names no
    /// known hit type, which the loader logs and drops.</summary>
    public SkillHitType[] HitTypes =>
        _hitTypes ??= CombatBuffHitRules.TryDecodeBits(HitTypeBits, out var types) ? types : [];
    /// <summary><c>hit_skill_id</c> — the skill that must have landed for the row to match, 0 for any.</summary>
    public uint HitSkillId { get; set; }
    /// <summary><c>hit_skill_tag_id</c> — a tag the landed skill has to carry, 0 for any.</summary>
    public uint HitSkillTagId { get; set; }
    /// <summary>
    /// <c>req_skill_id</c> — unused. Only combat_buffs 220 carries it, and that row has a null
    /// req_buff_id, so nothing registers it today: see the loader's warning.
    /// </summary>
    public uint ReqSkillId { get; set; }
    public uint BuffId { get; set; }
    /// <summary>
    /// <c>buff_from_source</c> — loaded, not read: it is identical to <see cref="BuffToSource"/> on 48
    /// of the 57 rows and reading it on the other nine would attribute those procs to the attacker as
    /// caster, whose buff-duration modifiers would then scale them. See <see cref="CombatBuffHitRules"/>.
    /// </summary>
    public bool BuffFromSource { get; set; }
    /// <summary><c>buff_to_source</c> — which side of the hit owns this entry, and therefore which unit
    /// it buffs unless <see cref="ReverseTargetOn"/> flips it. See
    /// <see cref="CombatBuffHitRules.FiresForOwner"/>.</summary>
    public bool BuffToSource { get; set; }
    /// <summary><c>reverse_target_on</c> — apply the buff to the other combatant, not to the unit that
    /// owns this entry. See <see cref="CombatBuffHitRules.BuffsOwner"/>.</summary>
    public bool ReverseTargetOn { get; set; }
    public uint ReqBuffId { get; set; }
    public bool IsHealSpell { get; set; }
}
