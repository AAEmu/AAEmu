using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjMonsterGroupHunt : QuestActTemplate
{
    public uint QuestMonsterGroupId { get; set; }
    public int Count { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }
    public uint HighlightDoodadId { get; set; }
    public int HighlightDoodadPhase { get; set; }

    private int Objective { get; set; }
    //public static int GroupHuntStatus { get; private set; } = 0;

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Debug("QuestActObjMonsterGroupHunt: QuestMonsterGroupId {0}, Count {1}, UseAlias {2}, QuestActObjAliasId {3}, HighlightDoodadId {4}, HighlightDoodadPhase {5}, quest {6}, objective {7}, Score {8}",
            QuestMonsterGroupId, Count, UseAlias, QuestActObjAliasId, HighlightDoodadId, HighlightDoodadPhase, quest.TemplateId, objective, quest.Template.Score);


        if (quest.Template.Score > 0) // Check if the quest use Template.Score or Count
        {
            quest.GroupHuntStatus = objective * Count; // Count в данном случае % за единицу
            quest.OverCompletionPercent = quest.GroupHuntStatus + quest.HuntStatus + quest.GatherStatus + quest.InteractionStatus;

            if (quest.Template.LetItDone)
            {
                if (quest.OverCompletionPercent >= quest.Template.Score * 1 / 2)
                    quest.EarlyCompletion = true;

                if (quest.OverCompletionPercent > quest.Template.Score)
                    quest.ExtraCompletion = true;
            }

            Logger.Debug("QuestActObjMonsterGroupHunt: QuestMonsterGroupId {0}, Count {1}, GroupHuntStatus {2}, OverCompletionPercent {3}, quest {4}, objective {5}", QuestMonsterGroupId, Count, quest.GroupHuntStatus, quest.OverCompletionPercent, quest.TemplateId, objective);

            Update();

            return quest.OverCompletionPercent >= quest.Template.Score;
        }

        if (quest.Template.LetItDone)
        {
            quest.OverCompletionPercent = objective * 100 / Count;

            if (quest.OverCompletionPercent >= 50)
                quest.EarlyCompletion = true;

            if (quest.OverCompletionPercent > 100)
                quest.ExtraCompletion = true;
        }
        Logger.Debug("QuestActObjMonsterGroupHunt: QuestMonsterGroupId {0}, Count {1}, quest {2}, objective {3}", QuestMonsterGroupId, Count, quest.TemplateId, objective);

        Update();

        return objective >= Count;
    }
    public override void Update()
    {
        Objective++;
    }
    public override bool IsCompleted()
    {
        return Objective >= Count;
    }
    public override int GetCount()
    {
        Logger.Info("Получим, информацию на сколько выполнено задание.");

        return Objective;
    }
    public override void ClearStatus()
    {
        //GroupHuntStatus = 0;
        Objective = 0;
        Logger.Info("Сбросили статус в ноль.");
    }
}
