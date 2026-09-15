using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// Maps a refused accept onto the client's quest-error table. Every code here closes a hung Accept
/// window the same way; the client just says which gate refused.
/// </summary>
public static class QuestAcceptFailRules
{
    public static QuestStatusFailed RequirementNotMet => QuestStatusFailed.UnitRequirementCheck;

    /// <summary>The level gate has a row of its own; race and start unit_reqs share the generic one.</summary>
    public static QuestStatusFailed LevelNotMet => QuestStatusFailed.LevelNotMatch;

    /// <summary>
    /// A guild public assignment is owned by the guild board, so a personal accept is refused as a
    /// blocked quest even when the client offered it from a quest starter.
    /// </summary>
    public static QuestStatusFailed PublicAssignmentBlocked => QuestStatusFailed.BlockedQuest;

    public static QuestStatusFailed MissingSource(QuestAcceptorType type) =>
        type == QuestAcceptorType.Doodad
            ? QuestStatusFailed.InvalidDoodad
            : QuestStatusFailed.InvalidNpcOrQuest;
}
