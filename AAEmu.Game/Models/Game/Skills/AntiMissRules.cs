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
///
/// One row is outside the scale, and its consequence is a recorded decision rather than an accident. Buff 807
/// (주문 방해) stores -3000 on <c>spell_anti_miss_mul</c>, ten times the -300 its own description ("시전
/// 시간을 30% 지연시키고 마법 성공률을 30% 감소") and its sibling <c>casting_time_mul</c> row (71, 300) both
/// promise, and no other blind row in the family is past the -1000 edge. <see cref="Multiplier"/> floors it
/// to 0: attribute 88 was read by nothing before this branch, so 주문 방해 used to delay casts only, and with
/// the row consumed its victim rolls every spell against a hit chance of 0 for the buff's duration — spells
/// stop landing altogether instead of 30% of them missing. The floor is kept because it is the only guard in reach
/// (<c>unit_attribute_limits</c> carries no row for 78, 83 or 88; 54 has one at -666, which is exactly what
/// keeps buff 4343's -700 on the stated scale) and because the family does ship out-of-range rows on the
/// positive side too, where an unclamped multiplier reads as "always hits" (buff 28738 stores 500000 on all
/// three ids). Reading a row past the edge as the authored value divided by ten would match 807's tooltip but
/// invent a scale the content never states, so it was rejected. The choice is pinned by
/// <c>AntiMissRulesTests.Buff807_IsFlooredToASilence_NotRescaledToTheThirtyPercentItReads</c> and
/// <c>AttackSpeedAttributeTests.RollCombatDice_Buff807sMinus3000Row_LeavesTheDebuffedCasterNoSpellHit</c>, and
/// named in the PR's behaviour-change note.
/// </remarks>
public static class AntiMissRules
{
    /// <summary>Rating meaning "no change": the multiplier is exactly 1.0.</summary>
    public const int Baseline = 1000;

    /// <summary>
    /// The multiplier to apply to one damage type's accuracy, <c>(rating + Baseline) / Baseline</c>, never
    /// negative. Buff 807 (주문 방해) stores -3000 on the spell id; see the remarks for why that row is
    /// floored to a silence instead of being rescaled to the 30% its description reads as.
    /// </summary>
    public static float Multiplier(long rating) => Math.Max(0f, (float)((rating + Baseline) / (double)Baseline));

    /// <summary>
    /// The chance out of 100 the hit roll is compared against, for an accuracy stat of
    /// <paramref name="accuracy"/> and the multiplier the unit carries for this damage type.
    /// </summary>
    public static float HitChance(float accuracy, float multiplier) => accuracy * multiplier;
}
