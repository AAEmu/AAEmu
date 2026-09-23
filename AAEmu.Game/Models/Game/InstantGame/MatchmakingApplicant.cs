using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.InstantGame;

public class MatchmakingApplicant(Character charObj, DateTime timeApplied)
{
    /// <summary>UTC moment the applicant queued; drives the queue expiry (content: apply_waiting_time).</summary>
    public DateTime TimeApplied { get; } = timeApplied;
    public Character CharObj { get; } = charObj;
}
