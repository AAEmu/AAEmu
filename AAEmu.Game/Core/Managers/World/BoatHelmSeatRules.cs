namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Seat rules for a BindSlave rider on a zone-simulated hull.
/// </summary>
/// <remarks>
/// The helm occupy pose and the wheel mesh are client-side. Crossing a zone seam while turning
/// makes the client send a type-1 actor move (stand / look) in world space. Writing that into
/// Local under the hull throws the rider off the seat, culls the hull, and the follow-up
/// character Create on the incoming zone paints a standing posture over the occupy anim.
/// </remarks>
public static class BoatHelmSeatRules
{
    public static bool IsSeatedOnSlave(bool hasBindSlaveSeat, bool parentIsSlave) =>
        hasBindSlaveSeat && parentIsSlave;

    /// <summary>
    /// BindSlave seats parent the character under the hull. Those type-1 coordinates are world
    /// space, not a seat offset.
    /// </summary>
    public static bool ShouldIgnoreActorMoveWhileSeated(bool seatedOnSlave) =>
        seatedOnSlave;

    /// <summary>
    /// The incoming zone Creates the rider standing, then we attach. Actor moves in that window
    /// unseat them there. Type-5 already carries the stick.
    /// </summary>
    public static bool ShouldForwardActorMoveToZone(bool seatedOnSlave) =>
        !seatedOnSlave;

    /// <summary>
    /// Soft AOI and region leave must not <c>SCUnitsRemoved</c> the hull a rider is sitting on.
    /// Re-streaming it sends attach with no occupy, which is a standing pose and a dead wheel.
    /// </summary>
    public static bool ShouldKeepStreamedHullForRider(bool isRidingThisHull) =>
        isRidingThisHull;

    /// <summary>
    /// Zone model-posture on the seated character is the standing Create. The client already has
    /// the occupy anim from BindSlave.
    /// </summary>
    public static bool ShouldRelayZoneModelPosture(bool targetIsSeatedOnSlave) =>
        !targetIsSeatedOnSlave;

    /// <summary>
    /// At the follow switch the rider gets its attach re-sent so the occupy pose survives the
    /// incoming zone's standing Create. The client still holds the same hull (same objId), so a
    /// second <c>SCSlaveBound</c> and re-applied occupy buffs only make it tear down and rebuild the
    /// helm binding — three <c>clear bound slave mount skill</c> cycles and a "bound received, but
    /// not requested" warning per crossing (client log 2026-09-02), felt as a hitch at the seam.
    /// </summary>
    public static bool ShouldRebindHelmAtFollowSwitch => false;
}
