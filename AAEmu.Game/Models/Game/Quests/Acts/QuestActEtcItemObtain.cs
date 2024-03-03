using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActEtcItemObtain : QuestActTemplate
{
    public uint ItemId { get; set; }
    public uint HighlightDoodadId { get; set; }
    public bool Cleanup { get; set; }

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Debug("QuestActEtcItemObtain");

        if (ParentQuestTemplate.Score > 0) // Check if the quest use Template.Score or Count
        {
            quest.ItemObtainStatus = objective * Count; // Count в данном случае % за единицу
            quest.OverCompletionPercent = quest.ItemObtainStatus + quest.ItemUseStatus + quest.HuntStatus + quest.GroupHuntStatus + quest.InteractionStatus;

            if (ParentQuestTemplate.LetItDone)
            {
                if (quest.OverCompletionPercent >= ParentQuestTemplate.Score * 3 / 5)
                    quest.EarlyCompletion = true;

                if (quest.OverCompletionPercent > ParentQuestTemplate.Score)
                    quest.ExtraCompletion = true;
            }

            return quest.OverCompletionPercent >= ParentQuestTemplate.Score;
        }

        if (ParentQuestTemplate.LetItDone)
        {
            quest.OverCompletionPercent = objective * 100 / Count;

            if (quest.OverCompletionPercent >= 60)
                quest.EarlyCompletion = true;

            if (quest.OverCompletionPercent > 100)
                quest.ExtraCompletion = true;
        }
        return objective >= Count;
    }
}
