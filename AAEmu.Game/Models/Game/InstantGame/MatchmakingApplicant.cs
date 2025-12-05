using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.InstantGame;

public class MatchmakingApplicant(Character charObj)
{
    public DateTime TimeApplied { get; } = DateTime.UtcNow;
    public Character CharObj { get; } = charObj;
}