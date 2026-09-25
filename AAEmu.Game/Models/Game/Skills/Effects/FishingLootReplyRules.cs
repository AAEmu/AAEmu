using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// Keeps the regular-fishing reel-up reply on the existing error paths.
/// A missing loot row is a content failure; a refused delivery is BagFull.
/// </summary>
public static class FishingLootReplyRules
{
    public static ErrorMessageType? Reply(bool targetPresent, bool packPresent, bool packHasLoot, bool delivered)
    {
        if (!targetPresent)
            return ErrorMessageType.InvalidTarget;
        if (!packPresent || !packHasLoot)
            return ErrorMessageType.Invalid;
        return delivered ? (ErrorMessageType?)null : ErrorMessageType.BagFull;
    }
}
