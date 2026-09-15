using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Validation for the content tables that name a <see cref="UnitAttribute"/> by id.
/// </summary>
/// <remarks>
/// Every loader casts its <c>unit_attribute_id</c> column straight to the enum, so a row naming an id
/// the enum does not carry lands as a bonus nobody can query: no property carries it and no formula
/// reads it. That is expected for the ids this server has not modelled yet, so the rows stay loaded
/// and the loader reports the ids once per table instead of once per row.
/// </remarks>
public static class UnitAttributeLoadRules
{
    /// <summary>
    /// The distinct ids that have no <see cref="UnitAttribute"/> member, ascending.
    /// </summary>
    /// <remarks>
    /// Negative ids are the content's own "no attribute here" marker (only
    /// <c>actability_groups.unit_attr_id = -1</c> uses it) and are not reported.
    /// </remarks>
    public static IReadOnlyList<uint> UnknownIds(IEnumerable<long> attributeIds)
    {
        ArgumentNullException.ThrowIfNull(attributeIds);

        var unknown = new SortedSet<uint>();
        foreach (var attributeId in attributeIds)
        {
            if (attributeId is < 0 or > uint.MaxValue)
                continue;
            var id = (uint)attributeId;
            if (!Enum.IsDefined(typeof(UnitAttribute), id))
                unknown.Add(id);
        }

        return unknown.ToList();
    }

    /// <summary>
    /// The single warning line for a table, naming the ids and what is lost with them.
    /// </summary>
    public static string Warning(string source, IReadOnlyList<uint> unknownIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(unknownIds);

        return $"{source}: {unknownIds.Count} unit attribute id(s) have no UnitAttribute member: " +
               $"{string.Join(", ", unknownIds)}. Their modifiers load but nothing can query them.";
    }
}
