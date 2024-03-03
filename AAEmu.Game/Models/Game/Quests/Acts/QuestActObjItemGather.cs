using System;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjItemGather : QuestActTemplate, IQuestActGenericItem // Сбор предметов
{
    public uint ItemId { get; set; }
    public uint HighlightDoodadId { get; set; }
    public int HighlightDoodadPhase { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }
    public bool Cleanup { get; set; }
    public bool DropWhenDestroy { get; set; }
    public bool DestroyWhenDrop { get; set; }

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Debug($"QuestActObjItemGather: ItemId {ItemId}, Count {Count}, UseAlias {UseAlias}, QuestActObjAliasId {QuestActObjAliasId}, HighlightDoodadId {HighlightDoodadId}, HighlightDoodadPhase {HighlightDoodadPhase}, quest {ParentQuestTemplate.Id}, objective {objective}, Score {ParentQuestTemplate.Score}");

        var res = false;
        var maxCleanup = ParentQuestTemplate.LetItDone ? Count * 7 / 5 : Count;
        if (ParentQuestTemplate.Score > 0) // Check if the quest use Template.Score or Count
        {
            quest.GatherStatus = objective * Count; // Count в данном случае % за единицу
            quest.OverCompletionPercent = quest.GatherStatus + quest.HuntStatus + quest.GroupHuntStatus + quest.InteractionStatus;

            if (ParentQuestTemplate.LetItDone)
            {
                if (quest.OverCompletionPercent >= ParentQuestTemplate.Score * 1 / 2)
                    quest.EarlyCompletion = true;

                if (quest.OverCompletionPercent > ParentQuestTemplate.Score)
                    quest.ExtraCompletion = true;
            }

            Update();

            res = quest.OverCompletionPercent >= ParentQuestTemplate.Score;
        }
        else
        {
            if (ParentQuestTemplate.LetItDone)
            {
                quest.OverCompletionPercent = objective * 100 / Count;

                if (quest.OverCompletionPercent >= 50)
                    quest.EarlyCompletion = true;

                if (quest.OverCompletionPercent > 100)
                    quest.ExtraCompletion = true;
            }

            Update();

            res = objective >= Count;
        }

        if (res && Cleanup)
            quest.QuestCleanupItemsPool.Add(new ItemCreationDefinition(ItemId, Math.Min(maxCleanup, objective)));

        return res;
    }
}
