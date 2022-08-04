using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActObjInteraction : QuestActTemplate
    {
        public WorldInteractionType WorldInteractionId { get; set; }
        public int Count { get; set; }
        public uint DoodadId { get; set; }
        public bool UseAlias { get; set; }
        public bool TeamShare { get; set; }
        public uint HighlightDoodadId { get; set; }
        public int HighlightDoodadPhase { get; set; }
        public uint QuestActObjAliasId { get; set; }
        public uint Phase { get; set; }
        
        public static int InteractionStatus = 0;

        public override bool Use(ICharacter character, Quest quest, int objective)
        {
            _log.Warn("QuestActObjInteraction");
            if (quest.Template.Score > 0) // Check if the quest use Template.Score or Count
            {
                InteractionStatus = objective * Count; // Count в данном случае % за единицу
                quest.OverCompletionPercent = InteractionStatus + QuestActObjMonsterGroupHunt.GroupHuntStatus + QuestActObjMonsterHunt.HuntStatus + QuestActObjItemGather.GatherStatus;

                if (quest.Template.LetItDone)
                {
                    if (quest.OverCompletionPercent >= quest.Template.Score * 3 / 5)
                        quest.EarlyCompletion = true;

                    if (quest.OverCompletionPercent > quest.Template.Score)
                        quest.ExtraCompletion = true;
                }
                _log.Debug("QuestActObjInteraction: DoodadId {0}, Count {1}, InteractionStatus {2}, OverCompletionPercent {3}, quest {4}, objective {5}",
                    DoodadId, Count, InteractionStatus, quest.OverCompletionPercent, quest.TemplateId, objective);
                return quest.OverCompletionPercent >= quest.Template.Score;
            }
            else
            {
                if (quest.Template.LetItDone)
                {
                    quest.OverCompletionPercent = objective * 100 / Count;

                    if (quest.OverCompletionPercent >= 60)
                        quest.EarlyCompletion = true;

                    if (quest.OverCompletionPercent > 100)
                        quest.ExtraCompletion = true;
                }
                _log.Debug("QuestActObjInteraction: DoodadId {0}, Count {1}, quest {2}, objective {3}",
                    DoodadId, Count, quest.TemplateId, objective);
                return objective >= Count;
            }
        }
    }
}
