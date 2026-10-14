namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// Which level-pack <c>doodad.g</c> rows World should author when
/// <c>doodad_spawns.json</c> omitted them. Does not invent coordinates.
/// </summary>
public static class LevelPackDoodadRules
{
    /// <summary>
    /// Permanent plant: scene bodies and quest talk/func doodads.
    /// Not tower DoodadAlmighty, not a cell ignore/open list, and not
    /// <c>game_schedule_doodads</c> (Christmas / weekend / festival — those
    /// wait for the schedule). Extra fish schools and vegetation stay on json.
    /// </summary>
    public static bool ShouldAuthorPermanent(
        bool clientDoodad,
        bool npcTypeModel,
        bool talkOrQuestFunc,
        bool towerAlmighty,
        bool ignoredPermanent,
        bool scheduledEvent)
    {
        if (towerAlmighty || ignoredPermanent || scheduledEvent)
            return false;
        return clientDoodad || npcTypeModel || talkOrQuestFunc;
    }

    /// <summary>
    /// Dungeon copy plant: the copy's cells are its whole permanent doodad set, so every row
    /// is authored except the ones a tower step, an ignore/open list or a schedule owns.
    /// </summary>
    public static bool ShouldAuthorInCopy(bool towerAlmighty, bool ignoredPermanent, bool scheduledEvent) =>
        !towerAlmighty && !ignoredPermanent && !scheduledEvent;
}
