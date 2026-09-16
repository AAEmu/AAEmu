namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// <c>buffs.reflection_chance</c>, <c>reflection_ratio</c>, <c>reflection_target_ratio</c> and the five
/// damage-type flags: a buff that sends a share of an incoming hit back at whoever dealt it.
/// </summary>
/// <remarks>
/// The three numbers are spelled out by the shipped descriptions, and they are not what the column names
/// suggest at a glance. <c>reflection_chance</c> is the percentage chance the reflection happens at all.
/// <c>reflection_target_ratio</c> is the share of the hit that goes back to the attacker — "the target" is
/// the target of the reflection. <c>reflection_ratio</c> is the share of the hit the <em>defender</em>
/// still takes, which is why the rows that leave it at 100 all say "반사 시 자신이 입는 피해는 감소되지
/// 않습니다" (when reflecting, the damage you take is not reduced). The content agrees on all three:
/// <list type="bullet">
/// <item>386 가시방패: <c>chance 100, ratio 100, target_ratio 50</c> — "받는 근접 피해율의 50%를
/// 반사합니다. 반사 시 자신이 입는 피해는 감소되지 않습니다."</item>
/// <item>4366 자기장 보호막: <c>100, 10, 100</c> — "받는 모든 피해율의 100%를 반사합니다. 반사 시 자신은
/// 10%의 피해를 입습니다."</item>
/// <item>26818 복수: <c>100, 50, 200</c> — "공격 피해의 200%를 반사하고, 반사 시 자신은 50%의 피해를
/// 입습니다."</item>
/// <item>7132 보물 수호자의 화염 방패: <c>100, 50, 50</c> — "받는 원거리 피해율의 50%를 반사합니다.
/// 반사 시 자신은 원거리 공격에 50%의 피해를 입습니다."</item>
/// </list>
/// <para>
/// The five flags say which kinds of incoming hit a row reflects, and every one of the 77 rows with a
/// chance sets at least one of them, so a row that sets none reflects nothing.
/// <c>reflection_ignore_attacker</c> (2 rows) and <c>reflection_ignore_defender</c> (17 rows) are not
/// modelled: the first asks for the reflected hit to bypass the attacker's defence, which is what applying
/// it straight to health already does, and the second is inert in the content — all 17 rows describe
/// themselves with <c>reflection_ratio</c> alone (21375 활력 방패: 불꽃 is <c>ratio 75, target 25</c> and
/// reads "반사 시 자신은 75%의 마법 피해를 입습니다"), so there is no observable effect to implement.
/// </para>
/// </remarks>
public static class DamageReflectionRules
{
    /// <summary>Rolls are out of 100: the column is a percentage and its shipped ceiling is 100.</summary>
    public const int ChanceDenominator = 100;

    /// <summary>The stored chance, floored at 0 (never) and capped at 100 (always).</summary>
    public static int EffectiveChance(int stored) => Math.Clamp(stored, 0, ChanceDenominator);

    /// <summary>
    /// Whether one hit reflects, given the row's <c>reflection_chance</c> and an already-drawn
    /// <paramref name="roll"/> in [0, <see cref="ChanceDenominator"/>).
    /// </summary>
    public static bool Rolls(int chance, int roll) => roll < EffectiveChance(chance);

    /// <summary>Whether a row with these flags reflects a hit of <paramref name="damageType"/>.</summary>
    public static bool AppliesTo(bool melee, bool spell, bool siege, bool ranged, bool heal, DamageType damageType)
    {
        return damageType switch
        {
            DamageType.Melee => melee,
            DamageType.Magic => spell,
            DamageType.Siege => siege,
            DamageType.Ranged => ranged,
            DamageType.Heal => heal,
            _ => false
        };
    }

    /// <summary>
    /// The damage the attacker takes back: <c>reflection_target_ratio</c> percent of the hit. 100 and 200
    /// are exact, because the arithmetic is integer and the percentage is the unit the column is authored
    /// in (386 returns half of the hit, 26818 returns double it).
    /// </summary>
    public static int ReflectedDamage(int damage, int ratio)
    {
        if (damage <= 0 || ratio <= 0)
            return 0;

        return (int)Math.Min((long)damage * ratio / 100, int.MaxValue);
    }

    /// <summary>
    /// The damage the defender still takes: <c>reflection_ratio</c> percent of the hit. The authored
    /// default is 100 — 64 of the 77 rows leave it there — and it passes the number through untouched,
    /// which is what keeps those rows byte-for-byte as they were.
    /// </summary>
    public static int DefenderDamage(int damage, int ratio)
    {
        if (ratio == 100)
            return damage;
        if (damage <= 0 || ratio <= 0)
            return 0;

        return (int)Math.Min((long)damage * ratio / 100, int.MaxValue);
    }
}
