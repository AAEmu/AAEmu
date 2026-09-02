using AAEmu.Game.Models.Game.Models;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Plant trim and live waterline recover for zone-simulated hulls.
/// </summary>
/// <remarks>
/// Spawn-site checks only decide whether there is water. ZoneAuthority hulls always
/// simulate — the dedicate floats them from a <c>ship_models</c> tube or from prefab
/// <c>buoy*</c> parts. Recover never plants below the surface; the standalone
/// mass-center / keel offset is spawn trim for Game, not a live Z. Recover never runs
/// while a driver is seated.
/// </remarks>
public static class BoatWaterlineRules
{
    /// <summary>
    /// Shortest pause between recovers. Same window as an overlap fail-safe, so a still-sinking
    /// body is replanted without fighting every type-4.
    /// </summary>
    public static int RecoverCooldownMs => BoatZoneSimRules.OverlapFollowFailSafeMs;

    /// <summary>
    /// How far below the surface counts as sunk. Same band as a seam shortfall, so wave noise
    /// on a tube hull is left alone.
    /// </summary>
    public static float SinkBandMetres => BoatSeamImpulse.MinCorrectionDeficit;

    /// <summary>
    /// Dedicated fallback floater on <c>ship_models</c>. Prefab <c>buoy*</c> parts are separate;
    /// when both tube columns are zero the hull has only those parts and a thin collision box.
    /// </summary>
    public static bool HasBuoyancyTube(float tubeLength, float tubeRadius) =>
        tubeLength > 0f || tubeRadius > 0f;

    public static bool HasBuoyancyTube(ShipModelV1 model) =>
        model != null && HasBuoyancyTube(model.TubeLength, model.TubeRadius);

    /// <summary>
    /// Standalone Game applies the mass-center / keel plant. ZoneAuthority never does — the
    /// dedicate already uses those numbers, and applying them again buries a boxship.
    /// </summary>
    public static bool ShouldApplyKeelPlant(bool zoneAuthority) =>
        !zoneAuthority;

    public static bool ShouldApplyKeelPlant(float tubeLength, float tubeRadius) =>
        !HasBuoyancyTube(tubeLength, tubeRadius);

    public static bool ShouldApplyKeelPlant(ShipModelV1 model) =>
        model != null && ShouldApplyKeelPlant(model.TubeLength, model.TubeRadius);

    /// <summary>
    /// Standalone Game plant: half a negative mass-center, minus keel height. Zero when the
    /// mass-center is at or above the origin.
    /// </summary>
    public static float KeelPlantOffset(float massCenterZ, float keelHeight) =>
        (massCenterZ < 0f ? massCenterZ / 2f : 0f) - keelHeight;

    public static float KeelPlantOffset(ShipModelV1 model) =>
        model == null ? 0f : KeelPlantOffset(model.MassCenterZ, model.KeelHeight);

    /// <summary>
    /// Live recover Z. Never below the surface, never below the hull that is already floating.
    /// </summary>
    public static float RecoverZ(float surfaceZ, float hullZ) =>
        MathF.Max(hullZ, surfaceZ);

    /// <summary>
    /// Never held off. The flag is the zone's whole ship <em>simulation</em> switch, not a
    /// "someone is at the wheel" flag, and a missing tube is no reason to withhold it — prefab
    /// <c>buoy*</c> parts are the floater on those rows.
    /// </summary>
    /// <remarks>
    /// Withholding it does not leave a hull floating quietly: with the simulation off the zone
    /// drives the hull from its network movement controller instead of physics, so a parked hull
    /// is pinned to the last pose World sent it and stops moving altogether. Measured on a
    /// tube hull as much as a prefab-buoy one — no heave, no list, nothing until the helm was
    /// taken, which is not what a moored boat does.
    ///
    /// So an unmanned hull that rides bow-up with a standing yaw is the simulation diverging and
    /// has to be fixed as that. Turning the simulation off hides it by making the hull a puppet,
    /// and costs every unmanned hull its flotation to do it.
    /// </remarks>
    public static bool ShouldHoldSimOff(bool hasBuoyancyTube, bool hasDriver)
    {
        _ = (hasBuoyancyTube, hasDriver);
        return false;
    }

    /// <summary>
    /// A hull that was left held off takes the switch as soon as it has a driver. Zone sim is
    /// the path whether or not <c>ship_models</c> has a tube.
    /// </summary>
    public static bool ShouldResumeHeldSim(bool hasBuoyancyTube)
    {
        _ = hasBuoyancyTube;
        return true;
    }

    /// <summary>
    /// Zone sim owns the waterline. World never restomps <c>WZShipControlChange</c> 0/1
    /// to plant type-4 rest — that rebuilt the PE on prefab-buoy hulls (Ostera live:
    /// 15 s of quiet sim, then sunk recover every cooldown once Z dropped 0.5 m).
    /// </summary>
    public static bool ShouldRecover(
        bool inSeam,
        long armedAgeMs,
        long recoverAgeMs,
        float surfaceZ,
        float hullZ,
        float speedOverGround,
        sbyte throttle,
        bool hasDriver,
        bool hasBuoyancyTube)
    {
        _ = inSeam;
        _ = armedAgeMs;
        _ = recoverAgeMs;
        _ = surfaceZ;
        _ = hullZ;
        _ = speedOverGround;
        _ = throttle;
        _ = hasDriver;
        _ = hasBuoyancyTube;
        return false;
    }
}
