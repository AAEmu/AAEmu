namespace AAEmu.Game.Models.Game.Skills;

/// <summary>Equipment slot selected by the authored <c>weapon_gcd_id</c> skill field.</summary>
internal enum WeaponGcdId
{
    Mainhand = 15,
    Offhand = 16,
    Ranged = 17
}

/// <summary>
/// Shared GCD length and its attack-speed multiplier.
/// </summary>
/// <remarks>
/// Attack speed (<c>attack_speed_mul</c>, 218) scales the shared GCD for all skills. The continuous rate
/// formula preserves fractional percentages (for example, +177 means 84.96% of the base GCD, displayed as
/// 85%). <c>GlobalCooldownMul</c> (74) is the fallback for units without the general attack-speed rating.
/// Cast speed affects cast duration and does not scale the shared GCD.
/// </remarks>
public static class SkillGcdRules
{
    /// <summary>The server default <c>default_gcd</c> stands for when nothing else is authored.</summary>
    public const int NpcDefaultGcd = 1500;

    /// <inheritdoc cref="NpcDefaultGcd"/>
    public const int PlayerDefaultGcd = 1000;

    /// <summary>
    /// The live shared GCD multiplier. General attack speed takes priority; the pre-existing global cooldown
    /// multiplier remains as a fallback for units that do not carry an attack-speed rating.
    /// </summary>
    public static double AttackSpeedGcdMultiplier(long attackSpeedRating, float globalCooldownMul) =>
        attackSpeedRating != 0
            ? SpeedMultiplierRules.AttackSpeedDelayFactor(attackSpeedRating)
            : globalCooldownMul / 100.0;

    /// <summary>
    /// The base GCD in milliseconds before <see cref="AttackSpeedGcdMultiplier"/> is applied.
    /// </summary>
    /// <remarks>
    /// Resolution order (matches the client skill columns and hold-repeat pacing):
    /// <list type="number">
    /// <item><c>custom_gcd</c> — when authored, this is the reduced recovery the tooltip names.
    /// With a <c>weapon_gcd_id</c> slot equipped, the base is multiplied by weapon speed / 1,000 ms
    /// (Whirlwind Slash 13282: custom 200 × 2H speed). <c>default_gcd</c> does not override it.</item>
    /// <item><c>default_gcd</c> — full weapon speed when a slot is selected, otherwise the server
    /// default (1,000 ms player / 1,500 ms NPC).</item>
    /// <item><c>weapon_gcd_id</c> alone — that slot's equipped speed when neither flag authored a length.</item>
    /// </list>
    /// An unknown positive weapon slot selector falls through to the default rather than inventing a length.
    /// </remarks>
    public static int ResolveSharedGcd(int customGcd, bool defaultGcd, int weaponGcdSpeed, bool isNpc)
    {
        if (customGcd > 0)
        {
            if (weaponGcdSpeed <= 0)
                return customGcd;

            return (int)Math.Round(
                customGcd * (double)weaponGcdSpeed / PlayerDefaultGcd,
                MidpointRounding.AwayFromZero);
        }
        if (defaultGcd)
        {
            if (weaponGcdSpeed > 0)
                return weaponGcdSpeed;

            return isNpc ? NpcDefaultGcd : PlayerDefaultGcd;
        }
        if (weaponGcdSpeed > 0)
            return weaponGcdSpeed;

        // Skills with no authored GCD fields still use the caster's server default.
        return isNpc ? NpcDefaultGcd : PlayerDefaultGcd;
    }
}
