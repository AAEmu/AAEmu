namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Whether the zone a hull is about to be handed to has a host that can simulate it.
/// </summary>
/// <remarks>
/// A hull announced to a zone no dedicate has loaded is simulated by nobody: it keeps the pose it had
/// when it crossed the seam, ignores the helm, and the riders cannot leave it, so the ship is stranded
/// mid-sea until they relog. The handoff therefore has to know up front whether anything is listening.
/// </remarks>
public static class BoatZoneHostGate
{
    /// <param name="zoneId">Zone key the hull is being handed to.</param>
    /// <param name="instanceId">World copy the hull lives in; 0 for the continent.</param>
    /// <param name="isZoneLoaded">Continent probe; null when the process has no zone hosts at all.</param>
    /// <param name="isZoneInstanceLoaded">Copy-aware probe, used when the hull is inside an instance.</param>
    /// <returns>
    /// True unless a probe answers that the zone is not loaded, so an absent probe never costs a ship.
    /// </returns>
    public static bool HasHost(
        uint zoneId,
        uint instanceId,
        Func<uint, bool> isZoneLoaded,
        Func<uint, uint, bool> isZoneInstanceLoaded)
    {
        if (zoneId == 0)
            return true;

        if (instanceId != 0 && isZoneInstanceLoaded != null)
            return isZoneInstanceLoaded(zoneId, instanceId);

        return isZoneLoaded?.Invoke(zoneId) ?? true;
    }
}
