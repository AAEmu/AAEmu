using System;
using System.Security.AccessControl;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjItemGroupGather : QuestActTemplate
{
    public uint ItemGroupId { get; set; }
    public bool Cleanup { get; set; }
    public uint HighlightDoodadId { get; set; }
    public int HighlightDoodadPhase { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }
    public bool DropWhenDestroy { get; set; }
    public bool DestroyWhenDrop { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug($"QuestActObjItemGroupGather: ItemGroupId {ItemGroupId}, Count {Count}, UseAlias {UseAlias}, QuestActObjAliasId {QuestActObjAliasId}, HighlightDoodadId {HighlightDoodadId}, HighlightDoodadPhase {HighlightDoodadPhase}, quest {ParentQuestTemplate.Id}, objective {objective}, Score {ParentQuestTemplate.Score}");

        var res = false;
        var maxCleanup = ParentQuestTemplate.LetItDone ? Count * 7 / 5 : Count;
        if (ParentQuestTemplate.Score > 0) // Check if the quest use Template.Score or Count
        {
            quest.GroupGatherStatus = objective * Count; // Count в данном случае % за единицу
            quest.OverCompletionPercent = quest.GroupGatherStatus + quest.HuntStatus + quest.GroupHuntStatus + quest.InteractionStatus;

            if (ParentQuestTemplate.LetItDone)
            {
                if (quest.OverCompletionPercent >= ParentQuestTemplate.Score * 1 / 2)
                    quest.EarlyCompletion = true;

                if (quest.OverCompletionPercent > ParentQuestTemplate.Score)
                    quest.ExtraCompletion = true;
            }

            Update(quest, questAct);

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

            Update(quest, questAct);

            res = objective >= Count;
        }

        if (res && Cleanup)
            quest.QuestCleanupItemsPool.Add(new ItemCreationDefinition(ItemGroupId, Math.Min(maxCleanup, objective)));

        return res;
    }

    public override void Update(Quest quest, IQuestAct questAct, int updateAmount = 1)
    {
        // base.Update(quest, questAct, updateAmount);
        // Objective count is already set by CheckAct
        Logger.Info($"{QuestActTemplateName} - QuestActItemGroupGather {Id} was updated by {updateAmount} for a total of {questAct.GetObjective(quest)}.");
    }
}
