namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Shared GCD length, and the attack-speed or cast-speed multiplier applied to it.
/// </summary>
/// <remarks>
/// <c>use_weapon_cooldown_time</c> selects the multiplier: attack speed (<c>GlobalCooldownMul</c>) when it
/// is set, cast speed (<c>CastTimeMul</c>) otherwise. Every one of the 38,043 shipped <c>skills</c> rows
/// carries <c>'f'</c>, so in 10.0.2.13 the weapon branch never runs and every skill scales by cast speed;
/// it is kept because the column is authored data and a row that sets it must mean what it says.
/// </remarks>
public static class SkillGcdRules
{
    /// <summary>The server default <c>default_gcd</c> stands for when nothing else is authored.</summary>
    public const int NpcDefaultGcd = 1500;

    /// <inheritdoc cref="NpcDefaultGcd"/>
    public const int PlayerDefaultGcd = 1000;

    public static float SharedGcdMultiplier(bool useWeaponCooldownTime, float globalCooldownMul, float castTimeMul)
    {
        if (useWeaponCooldownTime)
            return globalCooldownMul / 100f;
        return castTimeMul;
    }

    /// <summary>
    /// The base GCD in milliseconds before <see cref="SharedGcdMultiplier"/> is applied.
    /// </summary>
    /// <remarks>
    /// Order of authority, most specific first:
    /// <list type="number">
    /// <item><c>custom_gcd</c> — the skill states its own recovery outright (3,407 rows).</item>
    /// <item><c>weapon_gcd_id</c> — the skill declares the weapon class whose swing interval it follows.
    /// The column holds a <c>holdables</c> id on 327 rows and all three shipped values are weapons —
    /// 15 한손창 1100 ms (263 rows), 16 양손창 1200 ms (9) and 17 양손지팡이 1300 ms (55) — but 140 of
    /// the 327 also author their own <c>custom_gcd</c>, which is the more specific statement and wins
    /// above, so this tier actually decides the GCD for 187 skills.</item>
    /// <item><c>default_gcd</c> — "use the server default" (1,000 ms for a player caster, 1,500 ms for
    /// an NPC). 29,054 of the 29,673 rows with the flag carry <c>custom_gcd</c> 0, i.e. they mean
    /// exactly this.</item>
    /// </list>
    /// The 619 rows that set both <c>default_gcd</c> and <c>custom_gcd</c> used to be resolved in favour
    /// of the default; <c>custom_gcd</c> now wins, because it is the value authored for the skill.
    /// A weapon class the loader does not know (0, or an id outside <c>holdables</c>) falls through to
    /// the default rather than inventing a length.
    /// </remarks>
    public static int ResolveSharedGcd(int customGcd, bool defaultGcd, int weaponGcdSpeed, bool isNpc)
    {
        if (customGcd > 0)
            return customGcd;
        if (weaponGcdSpeed > 0)
            return weaponGcdSpeed;
        if (defaultGcd)
            return isNpc ? NpcDefaultGcd : PlayerDefaultGcd;
        return 0;
    }
}
