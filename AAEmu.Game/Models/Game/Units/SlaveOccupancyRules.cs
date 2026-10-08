namespace AAEmu.Game.Models.Game.Units;

/// <summary>
/// Identity of the hull a character is actually on, for the client-stream keep-alive.
/// </summary>
/// <remarks>
/// Retail lets a player walk their own deck without sitting. The ground contact the client reports
/// (actor.gcId) parents the character to the carrier, so <c>Transform.Parent</c> names the hull — or
/// one of the hull's parts, whose own parent is the hull. Either way the character is on that hull and
/// its client stream has to be held exactly like a rider's: without it the hull falls back to the pure
/// AOI band, and a single step onto the deck culls it and re-sends it.
/// </remarks>
public static class SlaveOccupancyRules
{
    /// <summary>
    /// True when the carrier a character is parented to — or the hull that carrier is itself attached
    /// to — is <paramref name="hullObjId"/>.
    /// </summary>
    public static bool IsOnHull(uint carrierObjId, uint carrierParentHullObjId, uint hullObjId)
        => hullObjId != 0 && (carrierObjId == hullObjId || carrierParentHullObjId == hullObjId);

    /// <summary>
    /// True when the hull a character recorded as the one it stands on is still the hull its parent link
    /// reaches.
    /// </summary>
    /// <remarks>
    /// The record has to be re-checked against the live link instead of trusted: taking another vehicle's
    /// helm (BindSlave) re-parents without clearing it, and a stale record would hold a hull the character
    /// has left streamed for as long as they stay in the region.
    /// </remarks>
    public static bool RecordedHullIsStillReached(
        uint recordedHullObjId, uint parentObjId, uint parentOfParentObjId)
        => IsOnHull(parentObjId, parentOfParentObjId, recordedHullObjId);

    /// <summary>
    /// How far a hull may be and still be the one a character is standing on, in metres.
    /// </summary>
    /// <remarks>
    /// The client parks the sentinel <c>actor.gcId = 1</c> ("the current parent") while it stands on
    /// something, so on some hulls the World is never told which hull it is. Standing on something is
    /// only ever reported with the hull underfoot, so adopting the nearest boat within this reach
    /// restores the parent that the keep-alive and the local-position rule both need. It is a
    /// feature-owned constant, deliberately small: a deck, not the harbour.
    /// </remarks>
    public const float StandingHintRadiusMetres = 15f;

    /// <summary>Squared reach for <see cref="StandingHintRadiusMetres"/>.</summary>
    public static float StandingHintRadiusSquared
        => StandingHintRadiusMetres * StandingHintRadiusMetres;

    /// <summary>True when a hull at this squared distance is within the standing reach.</summary>
    public static bool IsWithinStandingReach(float distanceSquared)
        => distanceSquared <= StandingHintRadiusSquared;
}
