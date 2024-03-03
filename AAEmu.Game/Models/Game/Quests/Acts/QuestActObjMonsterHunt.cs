using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjMonsterHunt : QuestActTemplate
{
    public uint NpcId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }
    public uint HighlightDoodadId { get; set; }
    public int HighlightDoodadPhase { get; set; }

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Debug($"QuestActObjMonsterHunt: NpcId {NpcId}, Count {Count}, UseAlias {UseAlias}, QuestActObjAliasId {QuestActObjAliasId}, HighlightDoodadId {HighlightDoodadId}, HighlightDoodadPhase {HighlightDoodadPhase}, quest {ParentQuestTemplate.Id}, objective {objective}");

        if (ParentQuestTemplate.Score > 0) // Check if the quest use Template.Score or Count
        {
            quest.HuntStatus = objective * Count; // Count в данном случае % за единицу
            quest.OverCompletionPercent = quest.HuntStatus + quest.GatherStatus + quest.GroupHuntStatus + quest.InteractionStatus;

            if (ParentQuestTemplate.LetItDone)
            {
                if (quest.OverCompletionPercent >= ParentQuestTemplate.Score * 1 / 2)
                    quest.EarlyCompletion = true;

                if (quest.OverCompletionPercent > ParentQuestTemplate.Score)
                    quest.ExtraCompletion = true;
            }

            Logger.Debug($"QuestActObjMonsterHunt: NpcId {NpcId}, Count {Count}, HuntStatus {quest.HuntStatus}, OverCompletionPercent {quest.OverCompletionPercent}, quest {ParentQuestTemplate.Id}, objective {objective}");

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
        Logger.Debug($"QuestActObjMonsterHunt: NpcId {NpcId}, Count {Count}, quest {ParentQuestTemplate.Id}, objective {objective}");

        Update();

        return objective >= Count;
    }
}
