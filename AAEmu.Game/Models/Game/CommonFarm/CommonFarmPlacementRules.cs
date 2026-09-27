namespace AAEmu.Game.Models.Game.CommonFarm;

/// <summary>
/// Why a public-farm crop placement was refused.
/// </summary>
public enum CommonFarmPlacementRefusal
{
    /// <summary>The placement is allowed.</summary>
    None = 0,

    /// <summary>
    /// Content defines no crop capacity for this farm type, so nothing can be said about whether
    /// there is room. This is a content gap, not a full farm.
    /// </summary>
    CapacityNotConfigured,

    /// <summary>The character already holds the configured number of crops of this farm type.</summary>
    CapacityReached,

    /// <summary>The doodad is not in the farm group's allowed list.</summary>
    DoodadNotAllowed
}

/// <summary>
/// The decision table for admitting a crop into a public farm.
/// </summary>
/// <remarks>
/// <para>
/// This is the gate every player placement passes through, so it is kept as a pure function of its
/// inputs and the inputs stay separable: whether a capacity is configured at all is a different
/// question from how large it is.
/// </para>
/// <para>
/// The two must not be collapsed. A farm group with no capacity row used to resolve to a capacity
/// of zero, and a zero capacity is not an empty farm: the count check passed while the player had
/// no crops yet and failed for every crop after the first, so the farm accepted exactly one crop,
/// permanently refused the rest with a "farm is full" message, and never said why. An unconfigured
/// capacity is therefore refused on its own terms, at every planted count, and never borrows the
/// "full" answer.
/// </para>
/// </remarks>
public static class CommonFarmPlacementRules
{
    /// <summary>
    /// Decides whether a crop may be planted.
    /// </summary>
    /// <param name="capacityConfigured">
    /// Whether content supplies a capacity for this farm type. <c>false</c> means the farm's size is
    /// unknown, which is not the same as a farm with no room.
    /// </param>
    /// <param name="capacity">The configured capacity. Ignored when <paramref name="capacityConfigured"/> is false.</param>
    /// <param name="plantedCount">Crops of this farm type the character already holds.</param>
    /// <param name="doodadAllowed">Whether the doodad is in the farm group's allowed list.</param>
    /// <returns>The reason for refusing, or <see cref="CommonFarmPlacementRefusal.None"/> to allow.</returns>
    public static CommonFarmPlacementRefusal Evaluate(
        bool capacityConfigured, uint capacity, int plantedCount, bool doodadAllowed)
    {
        // An unknown capacity is answered before the count is looked at, so a farm whose size
        // content never defined refuses the same way whether the player holds one crop or ten.
        if (!capacityConfigured)
            return CommonFarmPlacementRefusal.CapacityNotConfigured;

        // A count is never negative. Refusing keeps the comparison below from reading a negative
        // value as a very large unsigned one and calling a full farm.
        if (plantedCount < 0)
            return CommonFarmPlacementRefusal.CapacityReached;

        if ((uint)plantedCount >= capacity)
            return CommonFarmPlacementRefusal.CapacityReached;

        if (!doodadAllowed)
            return CommonFarmPlacementRefusal.DoodadNotAllowed;

        return CommonFarmPlacementRefusal.None;
    }
}
