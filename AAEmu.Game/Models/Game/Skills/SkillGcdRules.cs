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
    /// Resolution order:
    /// <list type="number">
    /// <item><c>default_gcd</c> — takes priority over <c>custom_gcd</c> and uses the selected weapon's
    /// speed when available, otherwise the server default (1,000 ms for a player caster, 1,500 ms for an NPC).</item>
    /// <item><c>custom_gcd</c> — the skill's base recovery (3,407 rows). If the skill uses weapon speed,
    /// the base recovery is multiplied by equipped weapon speed / 1,000 ms.</item>
    /// <item><c>weapon_gcd_id</c> — selects the weapon slot whose speed drives the GCD: 15 Mainhand,
    /// 16 Offhand, and 17 Ranged. Ranged skills that arm a GCD without this marker also use the ranged
    /// slot. If the selected slot has no weapon and no custom GCD is authored, the skill falls through
    /// to the server default. The data has 327 populated rows and uses these three values; 140 of those
    /// rows also author <c>custom_gcd</c>, which is scaled by the selected weapon speed when default_gcd is false.</item>
    /// </list>
    /// When both <c>default_gcd</c> and <c>custom_gcd</c> are set, the default GCD wins.
    /// An unknown positive weapon slot selector falls through to the default rather than inventing a length.
    /// </remarks>
    public static int ResolveSharedGcd(int customGcd, bool defaultGcd, int weaponGcdSpeed, bool isNpc)
    {
        if (defaultGcd)
        {
            if (weaponGcdSpeed > 0)
                return weaponGcdSpeed;

            return isNpc ? NpcDefaultGcd : PlayerDefaultGcd;
        }
        if (customGcd > 0)
        {
            if (weaponGcdSpeed <= 0)
                return customGcd;

            return (int)Math.Round(
                customGcd * (double)weaponGcdSpeed / PlayerDefaultGcd,
                MidpointRounding.AwayFromZero);
        }
        if (weaponGcdSpeed > 0)
            return weaponGcdSpeed;

        // Skills with no authored GCD fields still use the caster's server default.
        return isNpc ? NpcDefaultGcd : PlayerDefaultGcd;
    }
}
