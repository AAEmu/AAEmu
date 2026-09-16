namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// ExplodeBuff (special type 46) burns the target's beneficial effects away and pays damage for each one it
/// burns, the way <c>DispelEffect</c> removes good buffs and a damage effect adds damage.
/// </summary>
/// <remarks>
/// content, 10.0.2.13 game_decrypted: 3 rows, all reachable. 5009 (<c>value1</c> 1000, <c>value2</c> 20,
/// <c>value3</c> 20) is the special effect of 내부 충격 16410 and 내부 충격 20650, and 6411 (2000, 1, 1) hangs
/// off the Timeout trigger (<c>buff_triggers</c> 2194, event 6) of buff 449 내부 충격.
/// <para>
/// The two value slots read as the number of effects to burn, and <c>value1</c> as the damage each burned
/// effect is worth, because both shipped descriptions say so. Skill 16410 (내부 충격) reads "적의 이로운
/// 효과를 태워 소멸 시키며 #{min_damage}~#{max_damage}의 피해를 주고" — burns the enemy's beneficial effects
/// away and deals min~max damage — with no separate damage effect on the skill, so the 1000 must be the
/// per-effect damage and the 1000..20000 span is what the tooltip range is built from. Buff 449 reads
/// "시간 만료 시 대상에게 이로운 효과가 존재할 경우 1개의 강화 효과 소멸 및 피해" — on expiry, if the target
/// has a beneficial effect, one enhancement is destroyed and damage dealt — and its trigger row carries
/// exactly 1 in both slots.
/// </para>
/// <para>
/// Buff 449 carries <c>max_charge</c> 0 and both init charge columns 0, and none of the buffs skills 16410
/// and 20650 apply carries a charge either, so the charge branch of a damage effect is not what these rows
/// read. Both shipped rows set <c>value2</c> and <c>value3</c> to the same number, so which of the two is
/// the cap cannot be told apart; the larger one is used, and the lower end is not enforced — a target
/// holding fewer effects than the row names still has the ones it holds burned.
/// </para>
/// </remarks>
public static class ExplodeBuffRules
{
    /// <summary>One instance on the target, as the removal decision sees it.</summary>
    public readonly record struct BurnCandidate(int Index, BuffKind Kind, bool Passive, bool System);

    /// <summary>
    /// How many of the target's effects a row may burn. 0 or a negative pair means the row names no effect
    /// and nothing happens.
    /// </summary>
    public static int MaxBuffsToBurn(int value2, int value3) => Math.Max(0, Math.Max(value2, value3));

    /// <summary>
    /// The effects to burn: the beneficial ones that are neither passive nor system, lowest slot first so a
    /// repeated cast always burns the same ones. Passives and system buffs are part of what the unit is
    /// rather than something an enemy's skill can strip, and the two shipped descriptions both speak of
    /// 이로운 효과 (beneficial effects).
    /// </summary>
    public static List<BurnCandidate> SelectBuffs(IEnumerable<BurnCandidate> buffs, int maxCount)
    {
        if (buffs == null || maxCount < 1)
            return [];

        return buffs
            .Where(buff => buff.Kind == BuffKind.Good && !buff.Passive && !buff.System)
            .OrderBy(buff => buff.Index)
            .Take(maxCount)
            .ToList();
    }

    /// <summary>
    /// Damage for the effects burned, in exact integer arithmetic: no burned effect is 0 damage, and a row
    /// with no per-effect damage leaves the target untouched.
    /// </summary>
    public static int DamageFor(int burnedCount, int damagePerBuff) =>
        burnedCount <= 0 || damagePerBuff <= 0 ? 0 : burnedCount * damagePerBuff;
}
