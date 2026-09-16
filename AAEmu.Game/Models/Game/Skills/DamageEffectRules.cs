using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// <c>enum_percent_damage_resource_types</c>: which pool a <c>use_percent_damage</c> effect reads its
/// percentage from. The five rows of the table are 1 current_health, 2 max_health, 3 current_mana and
/// 4 max_mana.
/// </summary>
public enum PercentDamageResourceType
{
    CurrentHealth = 1,
    MaxHealth = 2,
    CurrentMana = 3,
    MaxMana = 4
}

/// <summary>
/// The four pools <see cref="PercentDamageResourceType"/> names.
/// </summary>
public static class UnitResourcePools
{
    /// <summary>
    /// The value of one pool on one unit, read on demand so that an effect which does not use a percentage
    /// never asks a unit for a pool it may have no data for (a template-less unit in a test has no MaxHp).
    /// </summary>
    public static int ValueOf(Unit unit, PercentDamageResourceType type) => type switch
    {
        PercentDamageResourceType.CurrentHealth => unit.Hp,
        PercentDamageResourceType.MaxHealth => unit.MaxHp,
        PercentDamageResourceType.CurrentMana => unit.Mp,
        PercentDamageResourceType.MaxMana => unit.MaxMp,
        _ => 0
    };
}

/// <summary>
/// The arithmetic <c>DamageEffect</c> does around the damage range it composes: the authored add-on terms
/// and the per-victim scales. Every rule here answers "what does this row change", and every one of them
/// returns the unchanged input when the row is absent, so a shipped effect that does not set the column
/// keeps its numbers to the bit.
/// </summary>
public static class DamageEffectRules
{
    /// <summary>The factor of a scale that is not in play: exactly 1.0f.</summary>
    public const float NeutralFactor = 1f;

    /// <summary>The add-on of a term that is not in play: exactly 0f.</summary>
    public const float NeutralTerm = 0f;

    /// <summary>
    /// One draw from the authored <c>percent_min</c>..<c>percent_max</c> band, as a percentage of the pool.
    /// The band is inclusive, so a band of 35..35 means 35 % on every hit, and an inverted band (no such row
    /// ships) reads as its lower bound rather than going negative.
    /// </summary>
    public static float PercentRoll(int percentMin, int percentMax, float roll)
    {
        if (percentMax <= percentMin)
            return percentMin;

        return percentMin + (percentMax - percentMin) * Math.Clamp(roll, 0f, 1f);
    }

    /// <summary>
    /// The extra damage a <c>use_percent_damage</c> effect adds to the hit: the rolled percentage of
    /// <paramref name="poolValue"/>, the pool the row names on the unit it names.
    /// </summary>
    /// <remarks>
    /// It is an add-on, not a replacement. The client tooltips spell that out on the rows that carry the flag
    /// next to a real weapon hit: skill 44265 방패 휘두르기: 바위 authors percent 1..2 on damage effect 13193
    /// and its text reads "피해량: #{avg_damage} + 자신의 최대 생명력 1%~2%만큼의 추가 피해" ("damage, plus extra
    /// damage equal to 1%~2% of your own maximum health"), and its 20..30 % twin (effect 13194) is the same
    /// add-on against normal monsters. The boss rows are add-ons with nothing underneath them: 다후타의 해일
    /// (effects 5000/5001, 30..40 % of the victim's maximum health, damage type siege) and its 150..200 %
    /// charge (effects 4718/4755) one-shot a player, and 자폭하기 (effect 7609, 80..90 % of the *current*
    /// health) is the self-destruct that reads "자신도 매우 큰 피해를 입게 됩니다".
    ///
    /// The pool is the victim's; <c>use_source_health</c> (the 10.0.2.13 name of the field the template calls
    /// <c>UseCurrentHealth</c>, 't' on 69 rows) switches it to the caster's, which is what makes the
    /// shield-swing rows add a share of the *caster's* health. Both flags being false — 10,510 of the 11,001
    /// rows — means the caller never reaches this method, so the range below is untouched.
    /// </remarks>
    public static float PercentDamageTerm(int percentMin, int percentMax, float roll, int poolValue)
    {
        var percent = PercentRoll(percentMin, percentMax, roll);
        if (percent <= 0f || poolValue <= 0)
            return NeutralTerm;

        return poolValue * percent / 100f;
    }

    /// <summary>
    /// The damage one hit is worth on the victim's aggro table (<c>damage_effects.aggro_multiplier</c>).
    /// </summary>
    /// <remarks>
    /// The column is a factor on the damage the hit dealt, and the tooltips of the rows that carry it say so.
    /// The two 방패 휘두르기 rows that author 10.0 (damage effects 11583 and 12248) read "위협수준 생성량 높음"
    /// — "high threat generation" — next to a shield bash that is a tank's opener; 3단 베기, 진공 폭발 and
    /// 방패 휘두르기: 돌풍 author 3.0 (37 rows in all); and the 77 rows that author 0.0 are the hits that must
    /// pull nothing: 핏물먹이의 돌개바람, 극한의 얼음, 오스트 마력탑의 소환물 흡수 and 빛나는 해안 기지 자동
    /// 대포 발사. The factor lands on the value handed to <c>Npc.OnDamageReceived</c>, which is the victim's
    /// own aggro table — the number the packet reports and the damage total are the real damage.
    ///
    /// 1.0 is the identity: <c>(int)(damage * 1.0f)</c> is that same integer for every damage this path can
    /// produce, so the 10,837 rows at the default keep the aggro they had.
    /// </remarks>
    public static int AggroValue(int damage, float aggroMultiplier) => (int)(damage * aggroMultiplier);

    /// <summary>
    /// Whether the hit rolls the item procs either side carries (<c>damage_effects.fire_proc</c>): the
    /// attacker's <c>HitAny</c> and the victim's <c>TakeDamageAny</c>.
    /// </summary>
    /// <remarks>
    /// 't' on 10,760 of the 11,001 rows, so the roll stays exactly where it was for almost everything. The 241
    /// rows that clear it are the ones a proc must not answer: 감아올리기, 크게 감아올리기 and the rest of the
    /// grapple and stance-swap family.
    /// </remarks>
    public static bool FiresProcs(bool fireProc) => fireProc;

    /// <summary>
    /// The per-victim scale <c>target_health_min</c>/<c>max</c>/<c>mul</c>/<c>add</c> applies to the rolled
    /// hit while the victim's health percentage sits inside the authored band.
    /// </summary>
    /// <remarks>
    /// The four columns are the health-side twin of <c>target_buff_tag_id</c>/<c>target_buff_bonus_mul</c>:
    /// a condition on the victim plus a scale to apply while it holds. That scale is authored as an identity
    /// on every row that ships a band — the three rows with <c>target_health_max</c> set (6094 and 6103 on
    /// 죽음의 손길 호출기술 and 아홉 불길 정령왕의 포효, band 0..10, and 16145, band 0..3) all carry
    /// <c>target_health_mul</c> 1.0 with <c>target_health_add</c> 0 — so nothing in 10.0.2.13 actually moves.
    ///
    /// An unset band (an upper bound of 0, which is what all 11,001 rows author on
    /// <c>target_health_min</c>) is read as "no band" rather than "0 %..unbounded". The 26 rows that carry
    /// <c>target_health_mul</c> 0 rely on that: they are 헤임달의 즉살기's countdown ticks (damage effects
    /// 10818-10827 and 10842-10851, fixed 1..10, fired from the plot's own "10".."1" events) and a
    /// zero-damage countdown is not what that plot is for.
    /// </remarks>
    public static float TargetHealthAdjust(
        float damage,
        int victimHealthPercent,
        int minPercent,
        int maxPercent,
        float mul,
        int add)
    {
        // An upper bound of 0 is the unset value, and an inverted band is no band.
        if (maxPercent <= 0 || maxPercent < minPercent)
            return damage;

        if (victimHealthPercent < minPercent || victimHealthPercent > maxPercent)
            return damage;

        return damage * mul + add;
    }
}
