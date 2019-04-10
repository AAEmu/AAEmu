using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActConReportJournal : QuestActTemplate
    {
        public override bool Use(Character character, Quest quest, int objective) // take reward
        {
            _log.Debug("QuestActConReportJournal");

            quest.Step++;
            // quest.Complete(0);
            character.Quests.Complete(quest.TemplateId, 0, false);

            return true;
        }
    }
}
