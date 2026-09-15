namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// The siege damage factors: <c>incoming_siege_damage_mul</c> (<c>enum_unit_attribute</c> 149), read off the
/// victim, and <c>siege_damage_mul</c> (261), read off the caster.
/// </summary>
/// <remarks>
/// Both are per-mille deltas on a factor of 1, the shape <c>Character.IncomingSpellDamageMul</c> and
/// <c>Character.SpellDamageMul</c> already use, and the buff tooltips confirm the scale: buff 21088 stores
/// -900 and reads "받는 근접/공성 피해율이 90% 감소", buff 17128 stores -800 and reads "받는 공성 피해율이 80%
/// 감소", buff 21902 stores -165 and reads "16.5% 감소", buff 14993 stores +400 and reads "받는 공성 피해율을
/// 40% 증가시킵니다". The same value/10 relationship holds on the offensive side: <c>siege_damage_mul</c>
/// (261) and <c>siege_dps</c> (260) both walk -400…+700 in steps of 100 across the twelve 검은 가시 감옥 stages
/// (buffs 29998-30009), i.e. -40%…+70%.
///
/// The floor at zero matters: five shipped rows sit at or below -1000 (buff 14857 at -1000, buffs 24837 and
/// 24950 at -7000 and -21000), which a plain (value + 1000) / 1000 turns negative. A negative factor would
/// not zero the hit, it would flip its sign and <c>ReduceCurrentHp</c> would heal the victim instead, so the
/// composition stops at zero — immunity, which is what those rows describe ("공성 피해에 효과적으로
/// 방어합니다").
/// </remarks>
public static class SiegeDamageRules
{
    /// <summary>Rating meaning "no change": the factor is exactly 1.0.</summary>
    public const int Baseline = 1000;

    /// <summary>
    /// The factor to multiply siege damage by: 1.0 when the unit carries no such row, 0 when the rows reach
    /// immunity, 1.7 for the strongest shipped offensive row (+700).
    /// </summary>
    public static float Factor(long rating) => Math.Max(0f, (float)((rating + Baseline) / (double)Baseline));
}
