namespace AAEmu.Game.Models.Game.Indun.Matching;

/// <summary>
/// Who may be handed a prepared copy, and whether the client may be told it has entered.
/// </summary>
public static class IndunMatchEnterRules
{
    /// <summary>
    /// Daily-entry gate for a prepared copy. Already-charged rejoins skip the counter.
    /// </summary>
    public static bool CanAdmit(bool alreadyChargedThisCopy, bool dailyEntryAllowed) =>
        alreadyChargedThisCopy || dailyEntryAllowed;

    /// <summary>
    /// Reentry / entered-squad state is published only after at least one member is admitted.
    /// Publishing first leaves a rejected player on the playing screen outside the dungeon.
    /// </summary>
    public static bool ShouldPublishEnter(int admittedCount) =>
        admittedCount > 0;
}
