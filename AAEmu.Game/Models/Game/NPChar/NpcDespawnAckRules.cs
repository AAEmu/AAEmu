namespace AAEmu.Game.Models.Game.NPChar;

/// <summary>
/// Whether retiring a mirrored NPC must wait for the zone to confirm
/// <c>ZWRemoveNpc</c> before World deletes the object.
/// </summary>
public static class NpcDespawnAckRules
{
    /// <summary>
    /// Zone-created mirrors wait: releasing the id before the dedicate finishes can recycle it
    /// onto a new unit. World-authored units (SpawnEffect army, interaction-set swaps, GM spawn)
    /// never get that confirmation — the dedicate ignores <c>WZNpcStartDespawn</c> on a living
    /// copy — so waiting leaves the World object up and remirrors it after the later force-remove.
    /// </summary>
    public static bool WaitForZoneRemoveAck(bool zoneAuthority, bool isWorldAuthored) =>
        zoneAuthority && !isWorldAuthored;
}
