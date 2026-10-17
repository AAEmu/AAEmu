using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// LetItDone Progress steps normally stay parked until a report act fires.
/// Quests that only finish with <see cref="QuestActConAutoComplete"/> (no report)
/// must leave Progress as soon as their objective acts are met.
/// </summary>
public static class QuestLetItDoneAdvanceRules
{
    /// <summary>
    /// Final Progress result for a LetItDone quest. A report path stays parked.
    /// An auto-complete quest with no report leaves Progress when its objectives are met
    /// and its rewards were given. The LetItDone overachieve gate (3/2) is not an input:
    /// an exact count is already complete.
    /// </summary>
    /// <param name="rewardsDistributed">True when <c>DistributeRewards</c> did not hold the step back.</param>
    /// <param name="objectiveMet">Act results, or the score check when the quest is scored.</param>
    public static bool ResolveProgressResult(bool rewardsDistributed, bool objectiveMet, bool autoCompletesWithoutReport)
    {
        if (!autoCompletesWithoutReport)
            return false;

        return rewardsDistributed && objectiveMet;
    }

    /// <summary>
    /// True when the quest carries <see cref="QuestActConAutoComplete"/> and has no
    /// report act (NPC, NPC group, doodad, or journal) that could move LetItDone forward.
    /// </summary>
    public static bool AutoCompletesWithoutReport(IEnumerable<QuestActTemplate> acts)
    {
        var hasAutoComplete = false;
        foreach (var act in acts)
        {
            switch (act)
            {
                case QuestActConReportNpc:
                case QuestActConReportNpcGroup:
                case QuestActConReportDoodad:
                case QuestActConReportJournal:
                    return false;
                case QuestActConAutoComplete:
                    hasAutoComplete = true;
                    break;
            }
        }

        return hasAutoComplete;
    }
}
