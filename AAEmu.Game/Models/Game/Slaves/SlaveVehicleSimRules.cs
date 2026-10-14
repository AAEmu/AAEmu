namespace AAEmu.Game.Models.Game.Slaves;

/// <summary>
/// Zone simulation authority for player vehicles: ships and land vehicles are inverted.
/// </summary>
/// <remarks>
/// A ship with <c>WZShipControlChange control=1</c> is driven by the zone from helm input (type 5).
/// The same packet on a land vehicle parks it (pedal/steer driven to zero with the handbrake) and
/// drops every inbound move World relays — so a driven cart must keep <c>control=0</c> and stay
/// client-authored (VehicleMoveType). Boat seam handoff / type-4 pose seeds belong only to
/// zone-simulated hulls.
/// </remarks>
public static class SlaveVehicleSimRules
{
    /// <summary>
    /// True when the zone must never be handed this slave's simulation switch.
    /// </summary>
    public static bool IsClientDrivenLandVehicle(SlaveKind kind) =>
        kind is SlaveKind.Tank or SlaveKind.Machine or SlaveKind.SiegeWeapon;

    /// <summary>
    /// True when World may send <c>WZShipControlChange control=1</c> for this hull.
    /// </summary>
    public static bool ShouldArmZoneSimulation(SlaveKind kind) =>
        !IsClientDrivenLandVehicle(kind) && kind != SlaveKind.SlaveEquipment;

    /// <summary>
    /// True when announce / zone-change must run the ship seam plant and sim-arm machine.
    /// </summary>
    public static bool ShouldUseBoatSeamHandoff(SlaveKind kind) =>
        ShouldArmZoneSimulation(kind);

    /// <summary>
    /// True when World may Create this slave in a dedicate.
    /// </summary>
    /// <remarks>
    /// Retail: land carts and ships both get a zone body. Driven carts stay
    /// <c>control=0</c> + type-2 (see <see cref="ShouldArmZoneSimulation"/>). Equipment
    /// children ride the parent's announce. Do not skip Create to paper over plant-Z thrash.
    /// </remarks>
    public static bool ShouldAnnounceToZone(SlaveKind kind) =>
        kind != SlaveKind.SlaveEquipment;

    /// <summary>
    /// True when a land-vehicle <c>VehicleMoveType</c> may be forwarded as WZ movement.
    /// </summary>
    /// <remarks>
    /// Retail driven cart: client type-2 → zone net controller. Sea hulls use type 5, not this gate.
    /// </remarks>
    public static bool ShouldRelayLandVehicleMoveToZone(SlaveKind kind) =>
        IsClientDrivenLandVehicle(kind);

    /// <summary>
    /// Whether an observer should receive a land-vehicle <c>SCOneUnitMovement</c> echo.
    /// </summary>
    /// <remarks>
    /// The driver's client already owns the wheeled PE from its own <c>CSMoveUnit</c>. Echoing that
    /// pose back through <c>GetAround</c> fights the local chassis (bumpy / stuck). Observers and
    /// passengers still need the relay.
    /// </remarks>
    public static bool ShouldEchoLandVehicleMoveTo(uint observerObjId, uint driverObjId) =>
        observerObjId != 0 && observerObjId != driverObjId;

    /// <summary>
    /// Zone collision floaters for a land hull must not reach its occupying driver — same class
    /// of fight as echoing the driver's own type-2 pose.
    /// </summary>
    public static bool ShouldEchoLandVehicleCollisionResultTo(
        uint observerObjId, uint driverObjId) =>
        ShouldEchoLandVehicleMoveTo(observerObjId, driverObjId);
}
