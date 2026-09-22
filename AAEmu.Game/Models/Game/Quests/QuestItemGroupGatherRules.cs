namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// One quest_item_group_items row set for an item: quest_item_group_items carries use_grade and
/// item_grade_id per row, and a grade-gated item lists every grade it accepts (group 85's 45217 is
/// rows for grades 2..12). <see cref="Grades"/> is empty when the item is not gated, which counts
/// every grade the way the original loader did.
/// </summary>
public readonly record struct QuestGroupItemEntry(uint ItemId, uint[] Grades);

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
    /// <summary>
    /// A gated item only counts copies whose grade the group lists for it: six groups (64, 65, 68,
    /// 83, 85, 94) hold 104 such rows and their names read "grade or above" ("(고급 이상)"), so
    /// summing the listed grades is what the group asks for. An ungated entry counts every grade.
    /// </summary>
    public static int CountInGroup(IEnumerable<QuestGroupItemEntry> groupItems, Func<uint, int, int> countOfGrade)
    {
        if (groupItems == null || countOfGrade == null)
            return 0;
        var total = 0;
        var seen = new HashSet<uint>();
        foreach (var entry in groupItems)
        {
            if (!seen.Add(entry.ItemId))
                continue;
            total += entry.Grades is { Length: > 0 }
                ? entry.Grades.Sum(grade => Math.Max(0, countOfGrade(entry.ItemId, (int)grade)))
                : Math.Max(0, countOfGrade(entry.ItemId, -1));
        }
        return total;
    }

    /// <summary>
    /// cleanup / destroy_when_drop remove at most the objective count, walking the group in
    /// content order, so a bag holding more than the quest needed keeps the surplus. Only the
    /// grades the objective counted are taken, so a lower-grade copy survives a grade-gated row.
    /// A grade of -1 means the item was not gated and every grade may be taken.
    /// </summary>
    public static IEnumerable<(uint ItemId, int Count, int Grade)> CleanupPlan(
        IEnumerable<QuestGroupItemEntry> groupItems,
        Func<uint, int, int> countOfGrade,
        int maxToRemove)
    {
        if (groupItems == null || countOfGrade == null)
            yield break;
        var remaining = maxToRemove;
        var seen = new HashSet<uint>();
        foreach (var entry in groupItems)
        {
            if (remaining <= 0)
                yield break;
            if (!seen.Add(entry.ItemId))
                continue;

            var grades = entry.Grades is { Length: > 0 } ? entry.Grades.Select(grade => (int)grade) : [-1];
            foreach (var grade in grades)
            {
                if (remaining <= 0)
                    yield break;
                var have = countOfGrade(entry.ItemId, grade);
                if (have <= 0)
                    continue;
                var take = Math.Min(have, remaining);
                remaining -= take;
                yield return (entry.ItemId, take, grade);
            }
        }
    }
}
