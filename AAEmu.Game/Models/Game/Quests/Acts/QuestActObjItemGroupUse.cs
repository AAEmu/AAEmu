using System.Security.AccessControl;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjItemGroupUse : QuestActTemplate
{
    public uint ItemGroupId { get; set; }
    public uint HighlightDoodadId { get; set; }
    public int HighlightDoodadPhase { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }
    public bool DropWhenDestroy { get; set; }

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Debug("QuestActObjItemGroupUse");
        if (ParentQuestTemplate.Score > 0) // Check if the quest use Template.Score or Count
        {
            quest.ItemGroupUseStatus = objective * Count; // Count в данном случае % за единицу
            quest.OverCompletionPercent = quest.ItemGroupUseStatus + quest.HuntStatus + quest.GroupHuntStatus + quest.InteractionStatus;

            if (ParentQuestTemplate.LetItDone)
            {
                if (quest.OverCompletionPercent >= ParentQuestTemplate.Score * 1 / 2)
                    quest.EarlyCompletion = true;

                if (quest.OverCompletionPercent > ParentQuestTemplate.Score)
                    quest.ExtraCompletion = true;
            }

            Update();

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

        Update();

        return objective >= Count;
    }
}
