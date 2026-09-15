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
    public bool BuffFromSource { get; set; }
    public bool BuffToSource { get; set; }
    /// <summary><c>reverse_target_on</c> — apply the buff to the other combatant, not to the unit that
    /// owns this entry. See <see cref="CombatBuffHitRules.BuffsOwner"/>.</summary>
    public bool ReverseTargetOn { get; set; }
    public uint ReqBuffId { get; set; }
    public bool IsHealSpell { get; set; }
}
