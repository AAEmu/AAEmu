using AAEmu.Game.Models.Game.Quests.Static;

using NLog;

namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// Maps a refused accept onto the client's quest-error table. Every code here closes a hung Accept
/// window the same way; the client just says which gate refused.
/// </summary>
public static class QuestAcceptFailRules
{
    public static QuestStatusFailed RequirementNotMet => QuestStatusFailed.UnitRequirementCheck;

    /// <summary>
    /// The level gate has a row of its own and the race mask shares the generic one. A Start component's
    /// unit_reqs row is not mapped here at all: it is answered with SCQuestUnitReqFailed, which carries
    /// the row's own result.
    /// </summary>
    public static QuestStatusFailed LevelNotMet => QuestStatusFailed.LevelNotMatch;

    /// <summary>
    /// A guild public assignment is owned by the guild board, so a personal accept is refused as a
    /// blocked quest even when the client offered it from a quest starter.
    /// </summary>
    public static QuestStatusFailed PublicAssignmentBlocked => QuestStatusFailed.BlockedQuest;

    /// <summary>
    /// How loud a refused accept is worth being. One the character asked for is a warning; the same
    /// refusal on a server-driven start - a starter sphere walked through, a chain's next quest - is
    /// ordinary traffic, and a below-level character walks through that sphere again and again.
    /// </summary>
    public static LogLevel RefusalLogLevel(bool answerClient) =>
        answerClient ? LogLevel.Warn : LogLevel.Trace;

    public static QuestStatusFailed MissingSource(QuestAcceptorType type) =>
        type == QuestAcceptorType.Doodad
            ? QuestStatusFailed.InvalidDoodad
            : QuestStatusFailed.InvalidNpcOrQuest;
}
