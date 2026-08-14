namespace AAEmu.Game.Models.Game.Units;

/// <summary>
/// Nation / mother-faction identity for unit_reqs. Race factions (Nuian 101, …) resolve to
/// their alliance (Nuia 148 / Haranya 149); alliance rows keep their own id when mother is 0.
/// </summary>
public static class UnitReqNation
{
    public static uint EffectiveNationId(uint factionId, uint motherId)
        => motherId != 0 ? motherId : factionId;

    /// <summary>
    /// <c>nation_member</c> with value1=0: character's nation must match the zone's faction
    /// (west zones 148, east 149). Empty zone faction never matches.
    /// </summary>
    public static bool IsNationMemberOfZone(uint effectiveNationId, uint zoneFactionId)
        => zoneFactionId != 0 && effectiveNationId == zoneFactionId;
}
