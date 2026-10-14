namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// How long an individual skill cooldown lasts after modifiers.
/// </summary>
/// <remarks>
/// The client starts its own countdown when SkillStarted lands, so the server must arm at
/// accept (not only at Cast fire) or a cancelled cast leaves the grey bar without a matching
/// server gate — and a long cast leaves the server accepting a re-press while the client is
/// still grey. <c>bypassGcd</c> only skips the shared GCD; skill cooldowns still arm (ItemProc /
/// NpcEvents rely on that). Attribute 74 (<c>GlobalCooldownMul</c>) is GCD / attack-speed only —
/// it must not scale per-skill cooldown duration.
/// </remarks>
public static class SkillCooldownDurationRules
{
    /// <summary>
    /// True when this cast should arm the skill's own cooldown at accept (SkillStarted / plot press).
    /// Independent of <c>bypassGcd</c> — that flag only gates the shared GCD.
    /// </summary>
    public static bool ShouldArmOnAccept(bool unsupportedCast, uint cooldownTime) =>
        !unsupportedCast && cooldownTime > 0;

    /// <summary>
    /// Skill-row cooldown after <c>skill_modifiers</c> CoolDown. No GlobalCooldownMul scaling.
    /// </summary>
    public static uint ResolveDuration(uint modifiedCooldownTime) => modifiedCooldownTime;
}
