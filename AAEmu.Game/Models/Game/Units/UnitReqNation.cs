namespace AAEmu.Game.Models.Game.Units;

/// <summary>
/// Nation / mother-faction identity for unit_reqs. Race factions (Nuian 101, …) resolve to
/// their alliance (Nuia 148 / Haranya 149); alliance rows keep their own id when mother is 0.
/// </summary>
public static class UnitReqNation
{
    /// <summary>
    /// First faction id of a player nation. Kinds 60/61 in the client evaluator
    /// Compare the unit's faction id against this constant;
    /// system_factions ends at id 221, so only runtime nation factions reach it.
    /// </summary>
    public const uint PlayerNationFactionIdStart = 1000;

    public static uint EffectiveNationId(uint factionId, uint motherId)
        => motherId != 0 ? motherId : factionId;

    /// <summary>
    /// <c>nation_member</c> (60) passes for a unit whose faction is a player nation; <c>nation_member_not</c>
    /// (61) is the negation. Neither reads value1. The owning content is the territory delivery and
    /// nation officer quests, and the nation-versus-alliance variants of the war zone portals.
    /// </summary>
    public static bool IsPlayerNationMember(uint factionId)
        => factionId >= PlayerNationFactionIdStart;
}
