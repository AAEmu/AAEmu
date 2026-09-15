namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Who drives a unit's position. Under zone authority the dedicate simulates every NPC it knows about,
/// and World keeps a mirror of it; a World-authored move of such a unit is a second driver, and the
/// zone's next movement record snaps it back - which reads as a unit that will not stay where it was
/// put. Only a unit nothing published to a zone is World's to move.
/// </summary>
public static class ZoneOwnedUnitRules
{
    /// <summary>True when the dedicate, not World, is the one driving this unit.</summary>
    public static bool IsDrivenByZone(bool zoneAuthority, bool isZoneMirrorNpc) =>
        zoneAuthority && isZoneMirrorNpc;
}
