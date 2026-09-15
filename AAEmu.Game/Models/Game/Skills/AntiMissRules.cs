namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// The anti-miss multipliers <c>melee_anti_miss_mul</c> / <c>ranged_anti_miss_mul</c> /
/// <c>spell_anti_miss_mul</c> (<c>enum_unit_attribute</c> 78/83/88): the attacker's own accuracy for one damage
/// type, as a per-mille delta on the accuracy stat the hit is rolled against.
/// </summary>
/// <remarks>
/// The content DB names the direction and the scale in the same sentence. Blind-type rows carry negative
/// values and describe a loss of accuracy — buff 26158 (실명) stores -700 and reads "물리공격 성공률이 70%
/// 감소됩니다", buff 388 (실명) stores -250 for "물리 공격 성공률이 25% 감소됨", buff 23039 (위상 변화) -500
/// for "명중률이 50% 감소합니다", buff 2466 -70 for "마법 성공률이 7% 감소", buff 2214 -60 for "6% 감소",
/// buff 23000 -10 for "공격 성공률 1% 감소", buff 15040 -100 for "모든 공격 성공율이 10% 감소합니다".
/// The positive side is the same scale: the 도서관 ring/necklace families (buffs 16604-16691) grant 5…32 and
/// their tooltips interpolate <c>#{ua_*_anti_miss_mul}%</c>, the form that renders a per-mille attribute as
/// value/10 percent — the same rendering buff 19039 relies on when it stores 200 on <c>melee_damage_mul</c>
/// and reads "모든 기술 피해 20% 증가".
///
/// So this multiplies <see cref="AAEmu.Game.Models.Game.Units.Unit.MeleeAccuracy"/> and its two siblings
/// rather than adding percentage points to them; 100 accuracy with a -700 row is 30, and the two readings
/// coincide for the 100 baseline the accuracy stat ships with.
/// </remarks>
public static class AntiMissRules
{
    /// <summary>Rating meaning "no change": the multiplier is exactly 1.0.</summary>
    public const int Baseline = 1000;

    /// <summary>
    /// The multiplier to apply to one damage type's accuracy. Floored at 0: buff 807 (주문 방해) stores -3000
    /// on the spell id, and a negative factor would turn the accuracy roll inside out instead of simply
    /// never hitting.
    /// </summary>
    public static float Multiplier(long rating) => Math.Max(0f, (float)((rating + Baseline) / (double)Baseline));

    /// <summary>
    /// The chance out of 100 the hit roll is compared against, for an accuracy stat of
    /// <paramref name="accuracy"/> and the multiplier the unit carries for this damage type.
    /// </summary>
    public static float HitChance(float accuracy, float multiplier) => accuracy * multiplier;
}
