namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// When <see cref="Skill.Use"/> honors <c>skills.cancel_ongoing_buffs</c> (StartSkill / mate UseSkill wipe).
/// </summary>
/// <remarks>
/// <c>bypassGcd</c> casts are not a player skill press: <see cref="Tasks.Skills.UseSkillTask"/>
/// (SpecialEffect SkillUse), auto-attack tasks, item procs, and NPC event casts all pass
/// <c>bypassGcd: true</c>. Nested cadence skills such as the 운율 tick ship
/// <c>cancel_ongoing_buffs</c> with a null exception tag; applying that wipe would clear every
/// <c>remove_on_start_skill</c> performance buff on the first tick. Player presses keep
/// <c>bypassGcd: false</c> and still cancel with the skill's exception tag.
/// </remarks>
public static class SkillStartCancelOngoingRules
{
    /// <summary>
    /// True only for a player-initiated cast whose template asks to cancel ongoing buffs.
    /// </summary>
    public static bool ShouldApply(bool bypassGcd, bool templateCancelOngoingBuffs) =>
        !bypassGcd && templateCancelOngoingBuffs;
}
