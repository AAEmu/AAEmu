using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActObjMonsterHunt : QuestActTemplate
    {
        public uint NpcId { get; set; }
        public int Count { get; set; }
        public bool UseAlias { get; set; }
        public uint QuestActObjAliasId { get; set; }
        public uint HighlightDoodadId { get; set; }
        public int HighlightDoodadPhase { get; set; }

        public static int HuntStatus = 0;

        public override bool Use(ICharacter character, Quest quest, int objective)
        {
            _log.Debug("QuestActObjMonsterHunt: NpcId {0}, Count {1}, UseAlias {2}, QuestActObjAliasId {3}, HighlightDoodadId {4}, HighlightDoodadPhase {5}, quest {6}, objective {7}",
                NpcId, Count, UseAlias, QuestActObjAliasId, HighlightDoodadId, HighlightDoodadPhase, quest.TemplateId, objective);

            if (quest.Template.Score > 0) // Check if the quest use Template.Score or Count
            {
                HuntStatus = objective * Count; // Count в данном случае % за единицу
                quest.OverCompletionPercent = HuntStatus + QuestActObjItemGather.GatherStatus + QuestActObjMonsterGroupHunt.GroupHuntStatus + QuestActObjInteraction.InteractionStatus;

                if (quest.Template.LetItDone)
                {
                    if (quest.OverCompletionPercent >= quest.Template.Score * 3 / 5)
                        quest.EarlyCompletion = true;

                    if (quest.OverCompletionPercent > quest.Template.Score)
                        quest.ExtraCompletion = true;
                }

                _log.Debug("QuestActObjMonsterHunt: NpcId {0}, Count {1}, HuntStatus {2}, OverCompletionPercent {3}, quest {4}, objective {5}",
                    NpcId, Count, HuntStatus, quest.OverCompletionPercent, quest.TemplateId, objective);
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
                _log.Debug("QuestActObjMonsterHunt: NpcId {0}, Count {1}, quest {2}, objective {3}",
                    NpcId, Count, quest.TemplateId, objective);
                return objective >= Count;
            }

        }
    }
}
