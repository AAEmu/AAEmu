namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// The gate World puts between two basic attacks (skills 2/3/4) by a caster that is not a character.
/// </summary>
/// <remarks>
/// A zone-driven NPC cast arrives as <c>ZWStartSkill</c> and is run through <c>Skill.Use</c>, which used a
/// flat 1 500 ms <c>SkillLastUsed</c> window for every such caster. That constant is World's, not the
/// content's: it ignored the skill's own cooldown and the unit's attack-speed rating, and it is the reason
/// a swing the zone asked for came back as <c>CooldownTime</c> (the live log has caster 792 asking for
/// skill 2 twice a second and being refused nine times in five seconds).
/// <para>
/// The interval now comes from the shipped helper — <c>SkillManager.GetAttackDelay</c>, the same call the
/// character and mate auto-attack tasks make — so it carries the skill template's own cooldown and the
/// unit's attack-speed rating. For a basic attack on a unit with no speed rows that is 1 300 ms (skill 2's
/// 300 ms cooldown plus the helper's 1 000 ms recovery), and a unit carrying <c>attack_speed_mul</c> scales
/// from there. The clamp is the one the character weapon-speed branch already uses.
/// </para>
/// </remarks>
public static class NpcSwingGateRules
{
    /// <summary>The interval used when <c>GetAttackDelay</c> has nothing to say (a zero or negative answer).</summary>
    public const int DefaultSwingIntervalMs = 1500;

    /// <summary>Same bounds the character auto-attack branch clamps its weapon speed to.</summary>
    public const int MinSwingIntervalMs = 400;
    public const int MaxSwingIntervalMs = 5000;

    /// <summary>
    /// The minimum spacing between two NPC basic attacks, from the delay
    /// <c>SkillManager.GetAttackDelay</c> computed for the attack.
    /// </summary>
    public static int SwingIntervalMs(double attackDelayMs)
    {
        if (double.IsNaN(attackDelayMs) || attackDelayMs <= 0)
            return DefaultSwingIntervalMs;

        return (int)Math.Clamp(Math.Round(attackDelayMs, MidpointRounding.AwayFromZero),
            MinSwingIntervalMs, MaxSwingIntervalMs);
    }

    /// <summary>
    /// Whether a <c>ZWStartSkill</c> request may skip World's shared cast gate entirely. An NPC's cadence
    /// belongs to the zone's AI, and <c>almighty</c> is the zone saying so for this cast; a request without
    /// it keeps the interval above.
    /// </summary>
    public static bool BypassesWorldGate(bool casterIsNpc, bool almighty) => casterIsNpc && almighty;
}
