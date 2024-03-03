using System.Configuration;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

internal class QuestActObjItemUse : QuestActTemplate
{
    public uint ItemId { get; set; }
    public uint HighlightDoodadId { get; set; }
    public int HighlightDoodadPhase { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }
    public bool DropWhenDestroy { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug("QuestActObjItemUse");
        if (ParentQuestTemplate.Score > 0) // Check if the quest use Template.Score or Count
        {
            quest.ItemUseStatus = objective * Count; // Count в данном случае % за единицу
            quest.OverCompletionPercent = quest.ItemUseStatus + quest.ItemObtainStatus + quest.HuntStatus + quest.GroupHuntStatus + quest.InteractionStatus;

            if (ParentQuestTemplate.LetItDone)
            {
                if (quest.OverCompletionPercent >= ParentQuestTemplate.Score * 1 / 2)
                    quest.EarlyCompletion = true;

                if (quest.OverCompletionPercent > ParentQuestTemplate.Score)
                    quest.ExtraCompletion = true;
            }

            Update(quest, questAct);

            return quest.OverCompletionPercent >= ParentQuestTemplate.Score;
        }

        if (ParentQuestTemplate.LetItDone)
        {
            quest.OverCompletionPercent = objective * 100 / Count;

            if (quest.OverCompletionPercent >= 50)
                quest.EarlyCompletion = true;

            if (quest.OverCompletionPercent > 100)
                quest.ExtraCompletion = true;
        }

        Update(quest, questAct);

        return objective >= Count;
    }
}
