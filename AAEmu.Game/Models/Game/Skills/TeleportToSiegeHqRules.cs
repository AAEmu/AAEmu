namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// The gate on the teleport_to_siege_hq (special type 65) effect: only a player character has a
/// resurrection point to be sent to, and the destination zone has to be simulated or the character is
/// stranded in a world nobody runs.
/// </summary>
/// <remarks>
/// content, 10.0.2.13 game_decrypted: 2 <c>special_effects</c> rows of type 65 (5584 and 9895, both all
/// zero), one per skill, and both skills are 진지로 이동 (17046 and 20950). Those two rows are identical
/// apart from their ids and icons, and their text is what says where the effect goes: "공성 영역에서만
/// 사용할 수 있고 사용 시 부활 지점으로 이동합니다" — usable only inside the siege area, and using it moves
/// you to the resurrection point. That is the pair <c>MoveToRezPointEffect</c> already resolves through
/// <c>PortalManager.GetDistrictReturnPoint</c> + <c>GetRespawnById</c>, so this effect lands the same way.
/// The data does not say which of the two skills belongs to the besieging side and which to the defending
/// one, so no per-side behaviour is chosen here.
/// </remarks>
public static class TeleportToSiegeHqRules
{
    /// <summary>
    /// True when the effect may move the caster. <paramref name="returnPointId"/> is the destination the
    /// character's return district resolved to — 0 means the district has none, and there is nowhere to go.
    /// </summary>
    public static bool CanTeleportTo(
        bool isPlayerCharacter,
        uint returnPointId,
        uint destinationZoneId,
        bool zoneAuthority,
        Func<uint, bool> isZoneLoaded)
        => isPlayerCharacter
           && returnPointId != 0
           && TeleportLandingRules.CanLandInZone(zoneAuthority, isZoneLoaded, destinationZoneId);
}
