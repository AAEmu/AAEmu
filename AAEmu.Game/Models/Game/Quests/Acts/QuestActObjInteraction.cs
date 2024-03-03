using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjInteraction : QuestActTemplate
{
    public WorldInteractionType WorldInteractionId { get; set; }
    public uint DoodadId { get; set; }
    public bool UseAlias { get; set; }
    public bool TeamShare { get; set; }
    public uint HighlightDoodadId { get; set; }
    public int HighlightDoodadPhase { get; set; }
    public uint QuestActObjAliasId { get; set; }
    public uint Phase { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        // TODO: Validate Phase when needed
        Logger.Debug("QuestActObjInteraction");
        if (ParentQuestTemplate.Score > 0) // Check if the quest use Template.Score or Count
        {
            quest.InteractionStatus = objective * Count; // Count в данном случае % за единицу
            quest.OverCompletionPercent = quest.InteractionStatus + quest.GroupHuntStatus + quest.HuntStatus + quest.GatherStatus;

            if (ParentQuestTemplate.LetItDone)
            {
                if (quest.OverCompletionPercent >= ParentQuestTemplate.Score * 1 / 2)
                    quest.EarlyCompletion = true;

                if (quest.OverCompletionPercent > ParentQuestTemplate.Score)
                    quest.ExtraCompletion = true;
            }
            Logger.Debug($"QuestActObjInteraction: DoodadId {DoodadId}, Count {Count}, InteractionStatus {quest.InteractionStatus}, OverCompletionPercent {quest.OverCompletionPercent}, quest {ParentQuestTemplate.Id}, objective {objective}");

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
        Logger.Debug($"QuestActObjInteraction: DoodadId {DoodadId}, Count {Count}, quest {ParentQuestTemplate.Id}, objective {objective}");

        Update(quest, questAct);

        return objective >= Count;
    }
}
