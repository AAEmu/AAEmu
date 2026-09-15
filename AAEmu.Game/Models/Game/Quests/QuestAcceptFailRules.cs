using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// Maps a refused accept onto the client's quest-error table.
/// Level, race, and start unit_reqs share one code so every hung Accept
/// window closes the same way.
/// </summary>
public static class QuestAcceptFailRules
{
    public static QuestStatusFailed RequirementNotMet => QuestStatusFailed.UnitRequirementCheck;

    public static QuestStatusFailed MissingSource(QuestAcceptorType type) =>
        type == QuestAcceptorType.Doodad
            ? QuestStatusFailed.InvalidDoodad
            : QuestStatusFailed.InvalidNpcOrQuest;
}
