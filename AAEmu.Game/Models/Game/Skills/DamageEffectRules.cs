using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// <c>enum_percent_damage_resource_types</c>: which pool a <c>use_percent_damage</c> effect reads its
/// percentage from. The four rows of the table are 1 current_health, 2 max_health, 3 current_mana and
/// 4 max_mana. Only the two health pools ship: all 491 rows that set <c>use_percent_damage</c> are type
/// 1 (39) or type 2 (452), so the mana arms exist against the enum rather than against any live row.
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
    /// The damage a weapon flag (<c>use_mainhand_weapon</c> 5,498 rows, <c>use_offhand_weapon</c> 31,
    /// <c>use_ranged_weapon</c> 736) contributes: the equipped weapon's own DPS.
    /// </summary>
    /// <remarks>
    /// <paramref name="unitDps"/> is the composed attribute — <c>Character.Dps</c> is
    /// <c>weapon.Dps * 1000 + Str / 5 * 1000</c> before its bonuses and <c>Npc.Dps</c> adds <c>Str / 10</c> —
    /// and it is only the fallback for a caster with no weapon item in the slot, which is every NPC and every
    /// unarmed unit. A weapon-flagged effect means "scale off the weapon"; the stat contribution already has
    /// its own term in the composition (<c>dps_inc_multiplier</c> over <c>melee_dps_inc</c> and its twins),
    /// so reading the composed attribute there counted the caster's strength twice.
    /// </remarks>
    public static float WeaponDps(float weaponDps, float unitDps) => weaponDps > 0f ? weaponDps : unitDps;

    /// <summary>
    /// The extra damage a <c>use_combat_resource</c> effect adds (16 rows): the pool the skill declares at
    /// <c>combat_resource_md</c>, plus a per-level and a per-DPS term.
    /// </summary>
    /// <remarks>
    /// The multiplier is a share of the pool, and the tooltips say so: 승자의 외침: 불꽃 authors
    /// <c>combat_resource_md</c> 1.0 on damage effect 13204 (skill 44269, <c>skills.combat_resource_id</c> 3 =
    /// 근성, ceiling 5,000) and reads "추가로 중첩된 근성 수치 100% + 자신의 최대 생명력 1%~2%만큼의 추가 근접
    /// 피해" — 100 % of the stacked 근성 plus the percent term — while its 12.5 twin (effect 13206, the
    /// monster-hunting variant) reads "근성 수치의 1250% 추가 피해". The pool is the current value, so a
    /// half-spent 근성 adds half as much.
    ///
    /// All 16 rows author 0 on <c>combat_resource_level_md</c> and <c>combat_resource_dps_md</c>, so the two
    /// extra terms are 0f in the shipped content; they are composed because that is the shape the three
    /// columns describe, and a row that does author them gets the term its own column asks for.
    /// </remarks>
    public static float CombatResourceTerm(
        int resourceValue,
        float resourceMd,
        int level,
        float levelMd,
        float unitDps,
        float dpsMd) =>
        resourceValue * resourceMd + level * levelMd + unitDps * dpsMd;

    /// <summary>
    /// The damage factor of a critical hit: the caster's typed critical bonus (<c>melee_critical_bonus</c> and
    /// its ranged and spell twins) plus this effect's own <c>critical_bonus</c>, less the victim's
    /// flexibility, over 100 — the expression this path already applied, with the effect's authored bonus
    /// folded in.
    /// </summary>
    /// <remarks>
    /// <c>critical_bonus</c> is 0 on 10,999 of the 11,001 rows, and adding 0f is exact, so those rows
    /// crit for exactly what they crit for before. The two rows that author 100 (damage effects 2225 and
    /// 2227, both multiplier 2.0 hits) double the critical bonus on top of whatever the caster carries.
    /// </remarks>
    public static float CriticalFactor(float unitCriticalBonus, int effectCriticalBonus, float victimFlexibility) =>
        1f + ((unitCriticalBonus + effectCriticalBonus) - victimFlexibility / 100f) / 100f;

    /// <summary>
    /// The damage a hit does against a victim carrying <c>target_buff_tag_id</c>: the authored multiplier,
    /// then the authored flat add (<c>target_buff_bonus</c>).
    /// </summary>
    /// <remarks>
    /// The add is flat damage, not another scale, and the rows that carry it say so in their tag
    /// descriptions: tag 5802 reads "경비견을 제외한 몬스터는 화염 방사기와 연쇄 광선탄에 추가 피해를
    /// 받습니다" and its rows add +3,000…+10,000 to a 7,000…24,000 hit, tag 5803 reads "철갑 뿔소 몬스터는
    /// 광선포에 의해 피해를 입지 않습니다" and its rows take 5,000 off a 15,000 hit, and 죽음의 바다
    /// (damage effects 12888/12900/12922, tag 4363 수영) adds 10,000 and 53,000 to a 500…6,000 hit, which is
    /// the kill its text describes ("물가에 있는 대상들을 모두 감전 시켜 사망하게 만듭니다").
    ///
    /// Only the 21 rows that author a non-zero add reach this; on the other 10,980 the add is exactly 0 and
    /// the result is the product the existing code already applied.
    /// </remarks>
    public static float TargetBuffDamage(float damage, float targetBuffBonusMul, int targetBuffBonus) =>
        damage * targetBuffBonusMul + targetBuffBonus;

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
