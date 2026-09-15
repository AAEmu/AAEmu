namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Guards for skill-driven teleports whose destination comes from data (a house, a return point
/// elected by the player). World routes players by <c>Transform.ZoneId</c> and fails closed, so a
/// landing in a zone that has no dedicated zone server would leave the character somewhere nobody
/// simulates - no NPCs, no movement, no way back. Refusing the cast is the safe answer.
/// </summary>
public static class TeleportLandingRules
{
    /// <summary>
    /// True when the destination can be simulated. <paramref name="isZoneLoaded"/> is the caller's
    /// "does this zone currently have a zone server" probe; it is null when no authority is configured,
    /// in which case there is nothing to refuse.
    /// </summary>
    public static bool CanLandInZone(bool zoneAuthority, Func<uint, bool> isZoneLoaded, uint destinationZoneId)
        => !zoneAuthority || isZoneLoaded == null || isZoneLoaded(destinationZoneId);

    /// <summary>
    /// Same-zone landings update Game and the client, but the zone still simulates the old
    /// position unless it is told the destination (the Blink path). Cross-instance uses
    /// SCLoadInstance; a zone change FinalizeTransform hands the character off — never blink
    /// the old zone after that.
    /// </summary>
    public static bool RelaysSameZoneBlink(bool stayInZone, bool zoneAuthority)
        => stayInZone && zoneAuthority;
}
