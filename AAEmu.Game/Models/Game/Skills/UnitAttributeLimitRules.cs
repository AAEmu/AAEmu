using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// One <c>unit_attribute_limits</c> row: the bounds one attribute's own composed value is kept inside.
/// </summary>
public readonly record struct UnitAttributeLimit(long Minimum, long Maximum);

/// <summary>
/// Clamps one unit attribute's composed value to that attribute's <c>unit_attribute_limits</c> row.
/// </summary>
/// <remarks>
/// <para>
/// Scope: a row bounds the attribute it names - the base the caller composes for that attribute plus
/// that attribute's own bonuses - and never the property that happens to read it. A property that reads
/// one attribute composes and clamps that attribute directly. A property that composes two bounded
/// attributes composes and clamps each of them on its own base and then adds the two, so neither row can
/// bite into the other's allowance: <c>Character.SpellCriticalBonus</c> (rows 31 and 152, both
/// -2000000000..4500) and <c>Character.SpellCriticalMul</c> (rows 86 and 151, both ..1000) are the two
/// such properties. The other four properties that call <c>CalculateWithBonuses</c> twice in a row pair a
/// bounded attribute with an unbounded one (<c>Character.SpellCritical</c>, and the three
/// <c>Incoming*DamageMul</c>), so no second row clamps a total that already carries another attribute.
/// </para>
/// <para>
/// Scale: a row can only bound a value composed in the row's own scale, and the table does not say which
/// scale it is in, so the decision is made per attribute here instead of being guessed from the value.
/// Three rows are in the client's absolute scale (100 = 1x, the value the client shows) while the server
/// composes the attribute as a 0-based delta and its consumer adds the 100 baseline itself:
/// </para>
/// <list type="bullet">
/// <item>
/// <c>drop_rate_mul</c> (140, 100..2000000000): <c>Character.DropRateMul</c> composes 0 as "no bonus" and
/// <c>LootPack</c>/<c>LootingContainer</c> add the 100 themselves as <c>(100 + DropRateMul) / 100</c>.
/// Clamping the delta up to the row's 100 would double every loot roll, so the row is skipped. The 125
/// shipped flat rows below the row's minimum (118 buff and 7 expedition-buff-grade rows, -80..+80 in
/// delta terms) keep working.
/// </item>
/// <item>
/// <c>exp_mul</c> (95, 0..500): the same shape, but here 0 is <em>inside</em> the row, so the row would
/// floor the deltas the content ships - -50 on buff 27888 and -500 on npc templates 13444, 16553 and
/// 16554 - at 0 and make those penalties inert. Skipped.
/// </item>
/// <item>
/// <c>living_point_gain_mul</c> (137, -100..2000000000): the same shape again; <c>Character</c> adds the
/// 100 baseline at the award site. Skipped.
/// </item>
/// </list>
/// <para>
/// <c>living_point_gain</c> (136, -2000000000..50) is the flat sibling and is not one of those three: the
/// server adds it to the award as it stands, with no baseline of its own, so it is composed in the row's
/// scale and stays clamped - which caps the shipped +100 (item 50762) and +200 (buff 28444) rows at 50.
/// </para>
/// <para>
/// An attribute with no row, and the three rows above, come back untouched.
/// </para>
/// </remarks>
public static class UnitAttributeLimitRules
{
    /// <summary>
    /// The rows that are in the client's absolute scale while the server composes the attribute as a
    /// 0-based delta, i.e. the rows this class deliberately does not apply. Each one is explained in the
    /// remarks above and has a test of its own.
    /// </summary>
    private static readonly HashSet<UnitAttribute> ClientScaleDeltaAttributes =
    [
        UnitAttribute.DropRateMul,
        UnitAttribute.ExpMul,
        UnitAttribute.LivingPointGainMul
    ];

    /// <summary>
    /// Whether the server composes this attribute as a 0-based delta while its row is in the client's
    /// absolute scale, so the row is skipped.
    /// </summary>
    public static bool IsClientScaleDelta(UnitAttribute attribute) => ClientScaleDeltaAttributes.Contains(attribute);

    /// <summary>
    /// The composed value, kept inside <paramref name="limit"/> when the attribute has a row that applies.
    /// </summary>
    /// <param name="composedValue">The value after every bonus for this attribute has been folded in.</param>
    /// <param name="attribute">The attribute <paramref name="composedValue"/> was composed for.</param>
    /// <param name="limit">That attribute's row, or null when the table has none.</param>
    public static double Clamp(double composedValue, UnitAttribute attribute, UnitAttributeLimit? limit)
    {
        if (limit is not { } bounds)
            return composedValue;

        if (IsClientScaleDelta(attribute))
            return composedValue;

        if (composedValue < bounds.Minimum)
            return bounds.Minimum;
        if (composedValue > bounds.Maximum)
            return bounds.Maximum;
        return composedValue;
    }
}
