using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjMonsterGroupHunt : QuestActTemplate
{
    public uint QuestMonsterGroupId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }
    public uint HighlightDoodadId { get; set; }
    public int HighlightDoodadPhase { get; set; }
    //public static int GroupHuntStatus { get; private set; } = 0;

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug($"QuestActObjMonsterGroupHunt: QuestMonsterGroupId {QuestMonsterGroupId}, Count {Count}, UseAlias {UseAlias}, QuestActObjAliasId {QuestActObjAliasId}, HighlightDoodadId {HighlightDoodadId}, HighlightDoodadPhase {HighlightDoodadPhase}, quest {ParentQuestTemplate.Id}, objective {objective}, Score {ParentQuestTemplate.Score}");


        if (ParentQuestTemplate.Score > 0) // Check if the quest use Template.Score or Count
        {
            quest.GroupHuntStatus = objective * Count; // Count в данном случае % за единицу
            quest.OverCompletionPercent = quest.GroupHuntStatus + quest.HuntStatus + quest.GatherStatus + quest.InteractionStatus;

            if (ParentQuestTemplate.LetItDone)
            {
                if (quest.OverCompletionPercent >= ParentQuestTemplate.Score * 1 / 2)
                    quest.EarlyCompletion = true;

                if (quest.OverCompletionPercent > ParentQuestTemplate.Score)
                    quest.ExtraCompletion = true;
            }

            Logger.Debug($"QuestActObjMonsterGroupHunt: QuestMonsterGroupId {QuestMonsterGroupId}, Count {Count}, GroupHuntStatus {quest.GroupHuntStatus}, OverCompletionPercent {quest.OverCompletionPercent}, quest {ParentQuestTemplate.Id}, objective {objective}");

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
        Logger.Debug($"QuestActObjMonsterGroupHunt: QuestMonsterGroupId {QuestMonsterGroupId}, Count {Count}, quest {ParentQuestTemplate.Id}, objective {objective}");

        Update(quest, questAct);

        return objective >= Count;
    }
}
