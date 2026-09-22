namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// Progress act QuestActObjItemGroupGather: hold count items of quest_item_groups item_group_id.
/// 56 enabled Progress rows over 56 quests. The client reader LoadQuestActObjItemGroupGatherDescs
/// (x2game-dev.dll FUN_39d3f0b0) reads id, check_exist, cleanup, count, destroy_when_drop,
/// drop_when_destroy, highlight_doodad_phase, highlight_doodad_id, item_group_id,
/// quest_act_obj_alias_id, use_alias. The counter is the bag total over the group, the same
/// absolute count QuestActObjItemGather keeps for one item.
/// </summary>
public static class QuestItemGroupGatherRules
{
    public static int CountInGroup(IEnumerable<uint> groupItems, Func<uint, int> countOf)
    {
        if (groupItems == null || countOf == null)
            return 0;
        var total = 0;
        foreach (var itemId in groupItems.Distinct())
            total += Math.Max(0, countOf(itemId));
        return total;
    }

    /// <summary>
    /// cleanup / destroy_when_drop remove at most the objective count, walking the group in
    /// content order, so a bag holding more than the quest needed keeps the surplus.
    /// </summary>
    public static IEnumerable<(uint ItemId, int Count)> CleanupPlan(
        IEnumerable<uint> groupItems,
        Func<uint, int> countOf,
        int maxToRemove)
    {
        if (groupItems == null || countOf == null)
            yield break;
        var remaining = maxToRemove;
        foreach (var itemId in groupItems.Distinct())
        {
            if (remaining <= 0)
                yield break;
            var have = countOf(itemId);
            if (have <= 0)
                continue;
            var take = Math.Min(have, remaining);
            remaining -= take;
            yield return (itemId, take);
        }
    }
}
