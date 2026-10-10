using AAEmu.Game.Models.Game.Skills.Effects.Enums;

namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// When a player-authored spawn is a companion the zone must keep with the summoner.
/// </summary>
/// <remarks>
/// <c>mate_state_id</c> alone is not enough — every shipped <c>spawn_effects</c> row carries stance
/// 1–3, including hostile quest summons and doodad pop-outs. Follow only when the row also sets
/// <c>use_summoner_faction</c> (player pets / companions) and the caster is a character. NPC-cast
/// army rows and doodad chests stay on their own AI.
/// </remarks>
public static class SummonCompanionRules
{
    /// <summary>
    /// True when the spawn row is a player companion and the zone should follow the summoner.
    /// </summary>
    public static bool ShouldFollowSummoner(bool casterIsCharacter, MateState mateState, bool useSummonerFaction) =>
        casterIsCharacter
        && useSummonerFaction
        && mateState is MateState.Passive or MateState.Protective or MateState.Aggressive;

    /// <summary>
    /// True when this World-authored NPC was created for <paramref name="characterId"/> and
    /// must be retired when that character leaves the copy.
    /// </summary>
    public static bool IsPlayerSummonedCompanion(bool isWorldAuthored, uint ownerId, uint characterId) =>
        isWorldAuthored && characterId != 0 && ownerId == characterId;
}
