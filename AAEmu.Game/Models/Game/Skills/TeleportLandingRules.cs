namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Open-world landings vs instance crossings. World hops (including 133↔183 inside one city)
/// restream the neighbourhood; a dungeon / system instance is entered through that copy so its
/// own units stream. A landing in a zone that has no dedicate would leave the character somewhere
/// nobody simulates — refuse those.
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

    /// <summary>
    /// Same zone and the same live instance: FinalizeTransform must not re-resolve the zone.
    /// </summary>
    public static bool StaysInZone(uint fromZoneId, uint toZoneId, uint fromInstanceId, uint toInstanceId) =>
        fromZoneId == toZoneId && fromInstanceId == toInstanceId;

    /// <summary>
    /// Same parent world, or the instance ids already match, is an open-world hop even when a
    /// doodad's stored instance disagrees with the player. A different world that hosts a dungeon
    /// copy is entered through that dungeon; any other hosted instance (battle field) loads that
    /// copy.
    /// </summary>
    public static TeleportLandingKind Classify(
        bool sameParentWorld,
        uint fromInstanceId,
        uint toInstanceId,
        bool destHasDungeon)
    {
        if (sameParentWorld || fromInstanceId == toInstanceId)
            return TeleportLandingKind.World;
        return destHasDungeon ? TeleportLandingKind.InstanceDungeon : TeleportLandingKind.InstanceOther;
    }
}

public enum TeleportLandingKind : byte
{
    World = 0,
    InstanceDungeon = 1,
    InstanceOther = 2
}
