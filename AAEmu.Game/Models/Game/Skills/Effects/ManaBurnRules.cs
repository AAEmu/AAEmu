namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// The pure part of <see cref="ManaBurnEffect"/>: the charge the row burns, from the sources it enables.
/// </summary>
/// <remarks>
/// The effect applied the level term unconditionally, so the 24 rows that ship <c>use_level_charge</c> false
/// burned a level's worth of mana they never authored. It read <c>base_min</c>/<c>base_max</c> and the level
/// pair and nothing else: <c>use_fixed_charge</c> (70 rows), <c>use_percent_charge</c> (15),
/// <c>percent_damage_resource_type_id</c> (all 100) and <c>damage_ratio</c> (38 non-zero) were all loaded and
/// unread.
/// </remarks>
public static class ManaBurnRules
{
    /// <summary>
    /// The pool a percentage source reads, from <c>mana_burn_effects.percent_damage_resource_type_id</c>
    /// (<c>enum_percent_damage_resource_types</c>).
    /// </summary>
    public enum ChargeResource
    {
        None = 0,
        CurrentHealth = 1,
        MaxHealth = 2,
        CurrentMana = 3,
        MaxMana = 4
    }

    /// <summary>
    /// The flat charge the row burns: <c>base_min</c>/<c>base_max</c>, drawn inclusively.
    /// </summary>
    /// <remarks>
    /// The pair is read when <c>use_fixed_charge</c> is set <em>or</em> either end is non-zero. In shipped
    /// content the two agree — the flag is false on 30 of the 100 rows and all 30 author 0/0, while the 70
    /// flagged rows all author a value — so the disjunction changes no shipped number. It is written that way
    /// because an authored base is itself the statement "this row burns a fixed charge": reading only the
    /// flag would silently zero a row that fills the pair and forgets the flag, which is what the effect did
    /// to every row before, and it is what the hand-built effects in the tests rely on.
    /// </remarks>
    public static int FixedCharge(bool useFixedCharge, int baseMin, int baseMax, int roll)
    {
        if (!useFixedCharge && baseMin == 0 && baseMax == 0)
            return 0;

        var lo = Math.Min(baseMin, baseMax);
        var hi = Math.Max(baseMin, baseMax);
        if (hi <= lo)
            return lo;
        return lo + Math.Clamp(roll, 0, hi - lo);
    }

    /// <summary>
    /// The charge a percentage source burns: <paramref name="percent"/> % of the pool the row names.
    /// </summary>
    /// <remarks>
    /// 15 rows enable it — mana-burn effects 91 and 94 are 50 % of the victim's current mana (resource type 1)
    /// — and they author <c>base_min</c>/<c>base_max</c> 0, so this is the whole charge for them. Resource id
    /// 0 has no enum member and adds nothing.
    /// </remarks>
    public static int PercentCharge(ChargeResource resource, int percent, int pool)
    {
        if (resource == ChargeResource.None || percent <= 0)
            return 0;

        return (int)(Math.Max(0, pool) * (percent / 100f));
    }

    /// <summary>
    /// The charge the level source adds: <c>level_md</c> × the caster's level rating, with the row's
    /// <c>level_va_start</c>/<c>level_va_end</c> band over the skill level.
    /// </summary>
    /// <remarks>
    /// Gated on <c>use_level_charge</c> (76 of 100 rows). The 24 rows that turn it off all author
    /// <c>level_md</c> 1.0 — the table's default, not a value anyone chose — so the gate is what keeps them
    /// from burning the caster's whole level rating.
    /// </remarks>
    public static float LevelCharge(bool useLevelCharge, float levelMd, float levelDps, int skillLevel,
        int levelVaStart, int levelVaEnd)
    {
        if (!useLevelCharge || levelMd <= 0f)
            return 0f;

        var lvlMd = levelDps * levelMd;
        var levelModifier = ((skillLevel - 1) / 49f * (levelVaEnd - levelVaStart) + levelVaStart) * 0.01f;

        return (levelModifier + 1) * lvlMd + 0.5f;
    }

    /// <summary>
    /// The charge the row burns, as the sum of the sources it enables, held at zero.
    /// </summary>
    public static int TotalCharge(int fixedCharge, int percentCharge, float levelCharge)
    {
        var total = Math.Max(0, fixedCharge) + Math.Max(0, percentCharge) + Math.Max(0f, levelCharge);
        return (int)total;
    }

    /// <summary>
    /// The health damage the burned mana turns into: <c>damage_ratio</c> per ten thousand of the charge.
    /// </summary>
    /// <remarks>
    /// <c>damage_ratio</c> is per ten-thousand, not per cent: 28 rows author 10000 (a full 100 %), 9 author
    /// 500 (5 %) and one 15000 (150 %). 62 rows author 0, which is "no health damage" rather than an
    /// unset column — mana burn that only drains mana is the common case in the table — so the term is
    /// exactly zero there and the effect leaves health alone.
    /// </remarks>
    public static int HealthDamage(int burnedMana, int damageRatio)
    {
        if (damageRatio <= 0 || burnedMana <= 0)
            return 0;

        return (int)(burnedMana * (damageRatio / 10000f));
    }
}
