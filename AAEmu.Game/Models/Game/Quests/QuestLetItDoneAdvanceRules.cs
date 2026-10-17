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
    /// Final Progress result after the LetItDone / score overrides: keep waiting for
    /// a report unless the quest auto-completes without one and its acts already passed.
    /// </summary>
    public static bool ResolveProgressResult(bool postOverrideResult, bool actsMet, bool autoCompletesWithoutReport)
    {
        if (!autoCompletesWithoutReport)
            return false;

        return postOverrideResult && actsMet;
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
