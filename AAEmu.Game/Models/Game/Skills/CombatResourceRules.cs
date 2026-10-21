namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// <c>max_combat_resource</c> (<c>enum_unit_attribute</c> 215): a signed delta on the ceiling of every
/// combat resource pool the unit holds.
/// </summary>
/// <remarks>
/// The rows are authored as whole points of a pool, and the one buff that names its pool proves the scale:
/// buff 22278 (정복) stores 1 and reads "광란의 중첩 개수가 1개 증가합니다" — 광란 is <c>combat_resources</c>
/// id 1 with a ceiling of 5, so the row raises that ceiling by exactly one. The rest of the family moves in
/// the same units: 145 of the 154 buff-owned rows store 1, the others 100…500 except two rows of -2, and the
/// four Slave rows (1500) and four BuffUnitModifier rows (-100…-500) belong to families this server does not
/// load.
///
/// The attribute applies to every pool at once because that is all a single unit attribute can mean; the
/// rows that look like hundreds belong to ship counters this server does not model as combat resources
/// (함포 효율성's 최대 보유 대포, 냉정한 마음's 최대 분노) rather than to a different scale.
/// </remarks>
public static class CombatResourceRules
{
    /// <summary>
    /// The client stores combat-resource totals in centi-points: seed/max are authored ×100, and
    /// <c>GetCombatResourceInfo</c> divides by 100 before Lua. <c>SCCombatResourcePoint.point</c> uses
    /// the same scale, so a content amount of 10 must leave the wire as 1000 or the bar reads 0.
    /// </summary>
    public const int WirePointScale = 100;

    /// <summary>
    /// The ceiling one pool accumulates to, after the unit's <c>max_combat_resource</c> rows. With no such
    /// row the result is the resource's own <c>combat_resources.max</c> unchanged, and a resource the loader
    /// does not know (max 0) keeps having no ceiling to clamp against rather than acquiring one from the
    /// attribute.
    /// </summary>
    public static int Ceiling(int resourceMax, long unitBonus) =>
        resourceMax > 0 ? (int)Math.Max(0, resourceMax + unitBonus) : 0;

    /// <summary>Content amount → the u64 the client expects on <c>SCCombatResourcePoint</c>.</summary>
    public static ulong ToWirePoint(int amount) =>
        (ulong)Math.Max(0, (long)amount * WirePointScale);

    /// <summary>
    /// <c>SCCombatResourcePoint.updateTime</c> is elapsed milliseconds into
    /// <c>combat_resources.recovery_cycle</c>. The client adds frame delta to that value and draws
    /// remaining as cycle − elapsed; sending the cycle itself therefore empties the HUD timer.
    /// </summary>
    /// <param name="resetRemainTime"><c>combat_resource_effects.reset_remain_time</c> — restart at 0.</param>
    /// <param name="recoveryCycleMs">Authored <c>recovery_cycle</c>, or 0 when the pool is untimed.</param>
    /// <param name="remainingMs">Server-side ms left on the current decay window.</param>
    public static int ToWireUpdateTime(bool resetRemainTime, int recoveryCycleMs, int remainingMs)
    {
        if (resetRemainTime || recoveryCycleMs <= 0)
            return 0;

        var elapsed = recoveryCycleMs - Math.Max(0, remainingMs);
        if (elapsed <= 0)
            return 0;
        return elapsed > recoveryCycleMs ? recoveryCycleMs : elapsed;
    }
}
