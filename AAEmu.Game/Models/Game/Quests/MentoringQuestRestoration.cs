using System.Collections.Frozen;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// Selects the original low-level dungeon mentoring quests restored on dungeon entry. Their zones,
/// level requirements, objectives, and rewards continue to come from game data.
/// </summary>
public static class MentoringQuestRestoration
{
    // Configured game-content quest_contexts: the four original mentee/mentor pairs for Okape,
    // Hieronimus, Akmit, and Marmas. This list selects restored content; it does not duplicate its rules.
    private static readonly FrozenSet<uint> RestoredQuestIds = new uint[]
    {
        6083, 6084,
        6087, 6088,
        6166, 6167,
        6168, 6169
    }.ToFrozenSet();

    public static IReadOnlyCollection<uint> QuestIds => RestoredQuestIds;

    public static bool IsRestoredQuest(uint questId) => RestoredQuestIds.Contains(questId);

    /// <summary>
    /// Applies only the restoration-specific entry conditions. Normal quest acceptance performs the
    /// same context and component checks again before creating the quest.
    /// </summary>
    public static bool CanStartOnDungeonEntry(
        QuestTemplate template,
        Character character,
        uint enteredZoneId,
        bool isDungeonInstance,
        bool isActive,
        bool completedToday,
        Func<QuestComponentTemplate, bool> meetsStartRequirements)
    {
        if (!isDungeonInstance || template == null || character == null || meetsStartRequirements == null)
            return false;
        if (!IsRestoredQuest(template.Id) || template.ZoneId != enteredZoneId || template.DetailId != QuestDetail.Daily)
            return false;
        if (isActive || completedToday || !template.MeetsContextRequirements(character))
            return false;

        var startComponents = template.GetComponents(QuestComponentKind.Start);
        return startComponents.Length > 0 && startComponents.All(meetsStartRequirements);
    }
}
