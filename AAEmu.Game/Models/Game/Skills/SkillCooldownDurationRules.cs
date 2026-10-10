namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// How long an individual skill cooldown lasts after modifiers.
/// </summary>
/// <remarks>
/// The client starts its own countdown when SkillStarted lands, so the server must arm at
/// accept (not only at Cast fire) or a cancelled cast leaves the grey bar without a matching
/// server gate — and a long cast leaves the server accepting a re-press while the client is
/// still grey. Duration scaling matches <c>Character.GlobalCooldownMul</c>: raw unit attribute 74
/// is already folded into that percentage, and <c>skill_modifiers</c> attribute
/// <c>Cooldown</c> is applied on top.
/// </remarks>
public static class SkillCooldownDurationRules
{
    /// <summary>
    /// True when this cast should arm the skill's own cooldown at accept (SkillStarted / plot press).
    /// </summary>
    public static bool ShouldArmOnAccept(bool bypassGcd, bool unsupportedCast, uint cooldownTime) =>
        !bypassGcd && !unsupportedCast && cooldownTime > 0;

    /// <summary>
    /// Applies attack-speed / CoolDown rating (<paramref name="globalCooldownMul"/> as a percent,
    /// 100 = unchanged) then the skill-modifier delta already folded into
    /// <paramref name="modifiedCooldownTime"/>.
    /// </summary>
    public static uint ResolveDuration(uint modifiedCooldownTime, float globalCooldownMul)
    {
        if (modifiedCooldownTime == 0)
            return 0;

        var mul = globalCooldownMul <= 0f ? 1f : globalCooldownMul / 100f;
        var scaled = (uint)Math.Max(0, Math.Round(modifiedCooldownTime * mul));
        return scaled;
    }
}
