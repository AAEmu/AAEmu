namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// The pure part of <see cref="ExtendChargeEffect"/>: how much charge a row adds to the absorption buff it
/// names, from the sources the row enables.
/// </summary>
/// <remarks>
/// The 23 shipped rows enable <c>use_percent_charge</c> or <c>use_dps_charge</c>, mostly both, and the total
/// is their sum. Every source below is gated on its own <c>use_*</c> column, so a row that enables nothing
/// adds nothing and the buff it names keeps exactly the charge it had.
/// </remarks>
public static class ExtendChargeRules
{
    /// <summary>
    /// The pool a percentage source reads, from <c>extend_charge_effects.percent_damage_resource_type_id</c>
    /// (<c>enum_percent_damage_resource_types</c>).
    /// </summary>
    /// <remarks>
    /// The column names the pool rather than always meaning health, and that matters here: 6 of the 23 rows
    /// read <c>max_mana</c> — extend-charge effects 30-33 are the 모두 치유: 파도 shields, whose own text
    /// reads "보호량: … + 자신의 최대 활력 5%", five percent of the caster's maximum mana. Reading the column
    /// as health would pour 5 % of a health bar into a mana shield.
    /// </remarks>
    public enum ChargeResource
    {
        None = 0,
        CurrentHealth = 1,
        MaxHealth = 2,
        CurrentMana = 3,
        MaxMana = 4
    }

    /// <summary>
    /// The charge a percentage source adds: <paramref name="percent"/> % of <paramref name="pool"/>.
    /// </summary>
    /// <remarks>
    /// <c>percent_damage_resource_type_id</c> is 0 on no shipped row, but id 0 has no enum member — it is
    /// "no pool authored", and a percentage that names no pool adds nothing rather than guessing health.
    /// The caller picks the pool: <c>use_source_health</c> decides whether it comes off the caster or off
    /// the unit being shielded.
    /// </remarks>
    public static int PercentCharge(ChargeResource resource, int percent, int pool)
    {
        if (resource == ChargeResource.None || percent <= 0)
            return 0;

        return (int)(Math.Max(0, pool) * (percent / 100f));
    }

    /// <summary>
    /// The charge a level source adds: <c>level_md</c> × the caster's level rating, with the row's
    /// <c>level_va_start</c>/<c>level_va_end</c> band applied over the skill's level.
    /// </summary>
    /// <remarks>
    /// The band and the +0.5 rounding are the composition <c>DamageEffect</c> and <c>HealEffect</c> already
    /// use for <c>use_level_damage</c>/<c>use_level_heal</c>, so the same row reads the same way in either
    /// place. <c>level_va_start</c> is 1 on 11 of the 12 rows that enable the source, so the band is a no-op
    /// there and the term is <c>level_md</c> times the level rating.
    /// </remarks>
    public static float LevelCharge(float levelMd, float levelDps, int skillLevel, int levelVaStart, int levelVaEnd)
    {
        if (levelMd <= 0f)
            return 0f;

        var lvlMd = levelDps * levelMd;
        var levelModifier = ((skillLevel - 1) / 49f * (levelVaEnd - levelVaStart) + levelVaStart) * 0.01f;

        return (levelModifier + 1) * lvlMd + 0.5f;
    }

    /// <summary>
    /// The charge a dps source adds: <c>dps_inc_multiplier</c> over the caster's own contribution to the
    /// damage type the row names, plus the weapon terms its flags enable.
    /// </summary>
    /// <remarks>
    /// <paramref name="dpsInc"/> is the stat the row's <c>damage_type_id</c> selects — melee_dps_inc for 2,
    /// spell_dps_inc for 5, ranged_dps_inc for 3, exactly the selection <c>DamageEffect</c> makes before its
    /// own <c>dps_inc_multiplier</c>, so the two chain the same way. <paramref name="dpsMultiplier"/> scales
    /// the weapon terms, and all three weapon flags are false on all 23 rows, so in shipped content this is
    /// the stat term alone.
    /// </remarks>
    public static float DpsCharge(float dpsIncMultiplier, int dpsInc, float dpsMultiplier, float weaponDps)
    {
        var stat = dpsInc * 0.001f * dpsIncMultiplier;
        var weapon = weaponDps * 0.001f * dpsMultiplier;
        return stat + weapon;
    }

    /// <summary>
    /// The charge the row adds, as the sum of the sources it enables.
    /// </summary>
    /// <remarks>
    /// The rolls and the composed floats come from the caller so this stays deterministic and testable.
    /// </remarks>
    public static int TotalCharge(
        bool useFixedCharge,
        int fixedRoll,
        bool usePercentCharge,
        int percentCharge,
        bool useLevelCharge,
        float levelCharge,
        bool useDpsCharge,
        float dpsCharge)
    {
        var total = 0f;

        if (useFixedCharge)
            total += Math.Max(0, fixedRoll);
        if (usePercentCharge)
            total += Math.Max(0, percentCharge);
        if (useLevelCharge)
            total += Math.Max(0f, levelCharge);
        if (useDpsCharge)
            total += Math.Max(0f, dpsCharge);

        return (int)total;
    }

    /// <summary>
    /// The charge the live instance ends up at, held at the buff's own ceiling.
    /// </summary>
    /// <remarks>
    /// <c>max_charge</c> is 0 on all but one of the buffs these rows name — "no ceiling authored" rather
    /// than "a ceiling of zero", the same reading <c>BuffStackRules.SummedCharge</c> takes for the stack
    /// rule — so clamping there would throw the added charge away. 22574 보호막 (extend-charge effect 13) is
    /// the exception at 20,000.
    /// </remarks>
    public static int ExtendedCharge(int liveCharge, int added, int maxCharge)
    {
        var sum = Math.Max(0, liveCharge) + Math.Max(0, added);
        return maxCharge > 0 ? Math.Min(sum, maxCharge) : sum;
    }
}
