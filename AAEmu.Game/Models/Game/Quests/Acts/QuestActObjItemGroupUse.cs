using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActObjItemGroupUse : QuestActTemplate
    {
        public uint ItemGroupId { get; set; }
        public int Count { get; set; }
        public uint HighlightDoodadId { get; set; }
        public int HighlightDoodadPhase { get; set; }
        public bool UseAlias { get; set; }
        public uint QuestActObjAliasId { get; set; }
        public bool DropWhenDestroy { get; set; }

        public static int ItemGroupUseStatus = 0;

        public override bool Use(ICharacter character, Quest quest, int objective)
        {
            _log.Warn("QuestActObjItemGroupUse");
            if (quest.Template.Score > 0) // Check if the quest use Template.Score or Count
            {
                ItemGroupUseStatus = objective * Count; // Count в данном случае % за единицу
                quest.OverCompletionPercent = ItemGroupUseStatus + QuestActObjMonsterHunt.HuntStatus + QuestActObjMonsterGroupHunt.GroupHuntStatus + QuestActObjInteraction.InteractionStatus;

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
