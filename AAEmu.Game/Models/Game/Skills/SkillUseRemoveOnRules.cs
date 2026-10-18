namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// When <c>SpecialEffect SkillUse</c> may raise <c>remove_on_use_skill</c> before scheduling the nested skill.
/// </summary>
/// <remarks>
/// Buff ticks apply effects with <see cref="CastBuff"/>. Romance (and similar) performances keep a
/// cadence manager buff whose tick is SkillUse → nested skill (운율 grant). Those stage and cadence
/// buffs themselves carry <c>remove_on_use_skill</c>, so raising the wipe on every tick ends the
/// song after the first cadence interval. A SkillUse from a real skill or plot cast still strips.
/// </remarks>
public static class SkillUseRemoveOnRules
{
    /// <summary>
    /// True when this SkillUse came from a player skill / plot cast, not a buff tick or buff trigger.
    /// </summary>
    public static bool ShouldTriggerRemoveOnUseSkill(CastAction castObj) =>
        castObj is not CastBuff;
}
