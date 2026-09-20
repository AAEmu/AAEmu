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
}
