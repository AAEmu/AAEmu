namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// When the shared GCD is armed for a cast.
/// </summary>
/// <remarks>
/// The shared GCD starts when the cast is accepted (SkillStarted / plot start), not when the
/// cast bar finishes. Instant skills already behaved that way because <c>Cast()</c> ran
/// immediately; cast-time skills used to arm only in <c>Cast()</c>, so a 1 s cast with a
/// 240 ms <c>custom_gcd</c> left a window where the next press was gated by the cast state
/// alone and Combo follow-ups lined up against the wrong clock. Plot-only paths already armed
/// on press for the same reason. Cancelling a cast after acceptance keeps the GCD — the
/// individual skill cooldown still arms only on a successful fire edge.
/// </remarks>
public static class SkillGcdArmRules
{
    /// <summary>
    /// True once the cast has a timeline id — every refusal path returns before that assign.
    /// </summary>
    public static bool ShouldArmOnAccept(bool bypassGcd, ushort tlId) =>
        !bypassGcd && tlId != 0;
}
