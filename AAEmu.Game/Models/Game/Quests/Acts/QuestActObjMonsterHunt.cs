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

        public static int GatherStatus = 0;

        public override bool Use(Character character, Quest quest, int objective)
        {
            _log.Debug("QuestActObjMonsterHunt: NpcId {0}, Count {1}, UseAlias {2}, QuestActObjAliasId {3}, HighlightDoodadId {4}, HighlightDoodadPhase {5}, quest {6}, objective {7}",
                NpcId, Count, UseAlias, QuestActObjAliasId, HighlightDoodadId, HighlightDoodadPhase, quest.TemplateId, objective);

            if (quest.Template.Score > 0) // Check if the quest use Template.Score or Count
            {
                QuestActObjItemGather.HuntStatus = objective * Count;
                quest.OverCompletionPercent = QuestActObjItemGather.HuntStatus + GatherStatus;

                if (quest.Template.LetItDone)
                {
                    if (quest.OverCompletionPercent >= quest.Template.Score * 3 / 5)
                        quest.EarlyCompletion = true;

                    if (quest.OverCompletionPercent > quest.Template.Score)
                        quest.ExtraCompletion = true;
                }

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
                return objective >= Count;
            }

        }
    }
}
