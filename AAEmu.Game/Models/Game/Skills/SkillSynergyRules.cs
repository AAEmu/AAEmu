namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// <c>skill_synergy_buff_tags</c>: the states on the <i>target</i> that make a skill's synergistic effects
/// land.
/// </summary>
/// <remarks>
/// 320 rows over 218 skills. The tags are target states — 10134 지옥의 창 with 수면 (sleep), 10151 with
/// 발묶임 (root) and 동결 (frozen), 10135 with 창 꽂힘 (impaled) — and the effects they unlock are extra
/// <c>damage_effects</c> rows: 122 rows flagged <c>synergy</c> across 21 skills, every one of which also
/// carries at least one un-flagged damage effect for the same skill (34354 육식의 칼날: 6 damage effects, 5
/// of them synergy; 40339 칼날 심판: 4 and 2). So a synergistic skill lands its base effect on any target
/// and its extra effects only on a tagged one, which is what "synergy" means here.
///
/// There is no magnitude column anywhere in the content for this: <c>buff_modifiers</c> carries 28 rows
/// flagged synergy and all of them are <c>in_duration</c>, not damage, and <c>skill_modifiers</c>' 61
/// synergy rows are cooldown/cast-time/range values. The extra damage is the extra effect rows.
///
/// <c>native</c> is not distinguished: both values appear against ordinary target states on the same
/// skill (10151 has 발묶임 native 't' and 동결 native 'f') and nothing in the content explains the split.
/// </remarks>
public static class SkillSynergyRules
{
    /// <summary>
    /// Whether an effect may land. A non-synergy effect always may; a synergy-flagged one needs the skill
    /// to declare synergy tags and the target to carry one of them.
    /// </summary>
    public static bool AllowsSynergyEffect(bool effectIsSynergy, bool skillHasSynergyTags, bool targetHasAnySynergyTag)
        => !effectIsSynergy || (skillHasSynergyTags && targetHasAnySynergyTag);
}
