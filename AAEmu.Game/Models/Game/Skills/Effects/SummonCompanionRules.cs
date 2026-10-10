using AAEmu.Game.Models.Game.Skills.Effects.Enums;

namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// When a player-authored spawn is a companion the zone must keep with the summoner.
/// </summary>
/// <remarks>
/// <c>spawn_effects.mate_state_id</c> is the companion stance (passive / protective / aggressive).
/// A character-cast row with one of those stances is a summon that should follow the summoner;
/// an NPC-cast row with the same stance is an event army and stays on its own AI. After combat
/// the zone otherwise leashes those summons back to the point they were created.
/// </remarks>
public static class SummonCompanionRules
{
    /// <summary>
    /// True when the spawn row is a player companion and the zone should follow the summoner.
    /// </summary>
    public static bool ShouldFollowSummoner(bool casterIsCharacter, MateState mateState) =>
        casterIsCharacter && mateState is MateState.Passive or MateState.Protective or MateState.Aggressive;

    /// <summary>
    /// True when this World-authored NPC was created for <paramref name="characterId"/> and
    /// must be retired when that character leaves the copy.
    /// </summary>
    public static bool IsPlayerSummonedCompanion(bool isWorldAuthored, uint ownerId, uint characterId) =>
        isWorldAuthored && characterId != 0 && ownerId == characterId;
}
