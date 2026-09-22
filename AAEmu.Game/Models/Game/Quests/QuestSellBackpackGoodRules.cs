namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// Progress act QuestActObjSellBackpackGood: sell a trade pack to an NPC of quest_monster_group_id.
/// 9 rows, 7 quests. content_item_type is 'Item' (6 rows, content_item_id is the pack template, e.g.
/// 43323 for quest 4665) or 'Tag' (3 rows on test quest 9011, content_item_id is tags.id 3197 carried
/// by the pack through item_tags). The client reader LoadQuestActObjSellBackpackGoodDescs
/// (x2game-dev.dll FUN_39d47870) reads id, content_item_type, content_item_id, count,
/// quest_act_obj_alias_id, quest_monster_group_id, use_alias.
/// </summary>
public static class QuestSellBackpackGoodRules
{
    public const string ContentTypeItem = "Item";
    public const string ContentTypeTag = "Tag";

    /// <summary>A content type the content database does not use never matches.</summary>
    public static bool ContentMatches(
        string contentItemType,
        uint contentItemId,
        uint soldItemId,
        Func<uint, uint, bool> hasItemTag)
    {
        if (contentItemId == 0 || soldItemId == 0)
            return false;
        return contentItemType switch
        {
            ContentTypeItem => soldItemId == contentItemId,
            ContentTypeTag => hasItemTag != null && hasItemTag(soldItemId, contentItemId),
            _ => false
        };
    }

    /// <summary>quest_monster_group_id 0 would accept any outlet; all 9 rows name a group.</summary>
    public static bool OutletMatches(uint questMonsterGroupId, bool npcInGroup)
        => questMonsterGroupId == 0 || npcInGroup;
}
