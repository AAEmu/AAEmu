namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// Progress acts QuestActObjMonsterContrHunt (8 rows, 7 quests) and QuestActObjMonsterContrGroupHunt
/// (21 rows, 9 quests): kills the player contributed to, not only kills the player or their tag team
/// owns. The contributors are the characters on the NPC's aggro table when it dies, which is the
/// record the server already ranks in QuestActObjAggro and reads for zone kill credit. Unlike
/// quest_act_obj_zone_kills and quest_act_obj_npc_kills these tables carry no team_share column,
/// so nothing is forwarded to team-mates who did not touch the NPC. The client readers
/// LoadQuestActObjMonsterContrHuntDescs (x2game-dev.dll FUN_39d3fbe0) and
/// LoadQuestActObjMonsterContrGroupHuntDescs (FUN_39d3fe80) read id, count, highlight_doodad_phase,
/// highlight_doodad_id, long_dist, npc_id or quest_monster_group_id, quest_act_obj_alias_id, use_alias.
/// </summary>
public static class QuestContributionHuntRules
{
    /// <summary>
    /// long_dist 'f' keeps the ordinary kill-credit range (LootingContainer.MaxLootingRange, the range
    /// Npc.DoDie applies to tag-team members). All 29 shipped rows are 't', so the gate is inert on
    /// content and only guards a NULL or a future 'f'.
    /// </summary>
    public static bool Contributes(bool longDist, float distance, float maxRange)
        => longDist || distance <= maxRange;
}
