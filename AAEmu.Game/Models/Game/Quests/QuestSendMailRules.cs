using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// quest_act_obj_send_mails (3 enabled rows: Start on quests 8952 and 9004, Reward on quest 150,
/// all category 12 "[TEST]"): item1_id..item3_id with count1..count3 are mailed to the owner. The
/// client reader LoadQuestActObjSendMailDescs (x2game-dev.dll FUN_39d462d0) reads id, count1..3,
/// item1_id..3, quest_act_obj_alias_id, use_alias. Row 1 mails 34820 x1; rows 2 and 3 name 45395,
/// which has no items row, so that slot is dropped and no mail goes out without a surviving slot.
/// </summary>
public static class QuestSendMailRules
{
    public static List<ItemCreationDefinition> Attachments(
        IReadOnlyList<(uint ItemId, int Count)> slots,
        Func<uint, bool> itemExists)
    {
        var result = new List<ItemCreationDefinition>();
        if (slots == null || itemExists == null)
            return result;

        foreach (var (itemId, count) in slots)
        {
            if (itemId == 0 || count <= 0 || !itemExists(itemId))
                continue;
            result.Add(new ItemCreationDefinition(itemId, count));
        }

        return result;
    }
}
