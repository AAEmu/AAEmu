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
    /// The ceiling one pool accumulates to, after the unit's <c>max_combat_resource</c> rows. With no such
    /// row the result is the resource's own <c>combat_resources.max</c> unchanged, and a resource the loader
    /// does not know (max 0) keeps having no ceiling to clamp against rather than acquiring one from the
    /// attribute.
    /// </summary>
    public static int Ceiling(int resourceMax, long unitBonus) =>
        resourceMax > 0 ? (int)Math.Max(0, resourceMax + unitBonus) : 0;
}
