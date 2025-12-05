using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.InstantGame;

public class MatchmakingApplicant
{
    public DateTime TimeApplied { get; }
    public Character CharObj { get; }

    public MatchmakingApplicant(Character charObj)
    {
        CharObj = charObj;
        TimeApplied = DateTime.UtcNow;
    }
}