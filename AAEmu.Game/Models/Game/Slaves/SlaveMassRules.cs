using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Slaves;

/// <summary>
/// Kit kilograms the zone does not already have from <c>ship_models.mass</c>.
/// </summary>
/// <remarks>
/// The dedicate sums attribute 188 on the hull and on each attached equipment child, then adds
/// that to the empty-hull model mass. Cargo per-pack mass is a different term and is not included
/// here. Occupants are computed on the zone from seat maps.
/// </remarks>
public static class SlaveMassRules
{
    /// <summary>
    /// Flat 188 rows from equipped items plus each child slave template.
    /// </summary>
    public static long KitAddedMass(
        IEnumerable<long> itemMassValues,
        IEnumerable<long> childSlaveMassValues)
    {
        return Sum(itemMassValues) + Sum(childSlaveMassValues);
    }

    /// <summary>
    /// Summon-card total: empty hull plus kit. The zone already owns the hull term.
    /// </summary>
    public static long TotalDisplayedMass(long hullModelMass, long kitAdded) =>
        hullModelMass + kitAdded;

    /// <summary>
    /// Flat Mass rows only. Percent modifiers and every other attribute are ignored.
    /// </summary>
    public static long MassFromBonuses(IEnumerable<BonusTemplate> bonuses)
    {
        if (bonuses == null)
            return 0;

        long sum = 0;
        foreach (var bonus in bonuses)
        {
            if (bonus?.Attribute == UnitAttribute.Mass && bonus.ModifierType == UnitModifierType.Value)
                sum += bonus.Value;
        }

        return sum;
    }

    private static long Sum(IEnumerable<long> values)
    {
        if (values == null)
            return 0;

        long sum = 0;
        foreach (var value in values)
            sum += value;
        return sum;
    }
}
