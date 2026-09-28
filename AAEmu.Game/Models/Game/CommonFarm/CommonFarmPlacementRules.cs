namespace AAEmu.Game.Models.Game.CommonFarm;

/// <summary>
/// Why a public-farm crop placement was refused.
/// </summary>
public enum CommonFarmPlacementRefusal
{
    /// <summary>The placement is allowed.</summary>
    None = 0,

    /// <summary>The doodad is not in the farm tab's allowed list.</summary>
    DoodadNotAllowed,

    /// <summary>
    /// Content defines no crop capacity for this farm tab, so nothing can be said about whether
    /// there is room. This is a content gap, not a full farm.
    /// </summary>
    CapacityNotConfigured,

    /// <summary>The character already holds the configured number of crops of this farm tab.</summary>
    CapacityReached
}

/// <summary>
/// The decision table for admitting a crop into a public farm.
/// </summary>
/// <remarks>
/// <para>
/// This is the gate every player placement passes through, so it is kept as a pure function of its
/// inputs and the inputs stay separable: whether a crop belongs in a tab at all is a different
/// question from how large that tab is.
/// </para>
/// <para>
/// Neither question may be collapsed into the other. A farm tab with no capacity row used to
/// resolve to a capacity of zero, and a zero capacity is not an empty farm: the count check passed
/// while the player had no crops yet and failed for every crop after the first, so the farm accepted
/// exactly one crop, permanently refused the rest with a "farm is full" message, and never said why.
/// An unconfigured capacity is therefore refused on its own terms, at every planted count, and never
/// borrows the "full" answer.
/// </para>
/// <para>
/// The order below is deliberate. The allowed list is asked <b>first</b>, because it is the only
/// question whose answer does not depend on a number content may not have supplied. Asking it later
/// meant a farm tab with no capacity row reported its content gap for a crop that was never
/// plantable there, which is a worse message for the player and a log line naming a content problem
/// where none exists.
/// </para>
/// </remarks>
public static class CommonFarmPlacementRules
{
    /// <summary>
    /// Decides whether a crop may be planted.
    /// </summary>
    /// <param name="doodadAllowed">
    /// Whether the doodad is in the farm tab's allowed list. Content states this per tab, so it is
    /// knowable even for a tab whose size is unknown.
    /// </param>
    /// <param name="capacityConfigured">
    /// Whether content supplies a capacity for this farm tab. <c>false</c> means the tab's size is
    /// unknown, which is not the same as a tab with no room.
    /// </param>
    /// <param name="capacity">The configured capacity. Ignored when <paramref name="capacityConfigured"/> is false.</param>
    /// <param name="plantedCount">Crops of this farm tab the character already holds.</param>
    /// <returns>The reason for refusing, or <see cref="CommonFarmPlacementRefusal.None"/> to allow.</returns>
    public static CommonFarmPlacementRefusal Evaluate(
        bool doodadAllowed, bool capacityConfigured, uint capacity, int plantedCount)
    {
        // Asked first, and off content alone. A crop that does not belong in this tab is refused
        // whatever the tab's size is, so nothing about the capacity — present, absent or zero — can
        // change the answer or be reported in its place.
        if (!doodadAllowed)
            return CommonFarmPlacementRefusal.DoodadNotAllowed;

        // An unknown capacity is answered before the count is looked at, so a tab whose size content
        // never defined refuses the same way whether the player holds one crop or ten.
        if (!capacityConfigured)
            return CommonFarmPlacementRefusal.CapacityNotConfigured;

        // A count is never negative. Refusing keeps the comparison below from reading a negative
        // value as a very large unsigned one and calling a full farm.
        if (plantedCount < 0)
            return CommonFarmPlacementRefusal.CapacityReached;

        if ((uint)plantedCount >= capacity)
            return CommonFarmPlacementRefusal.CapacityReached;

        return CommonFarmPlacementRefusal.None;
    }
}
