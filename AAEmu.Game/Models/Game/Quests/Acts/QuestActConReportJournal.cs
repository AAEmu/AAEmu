using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActConReportJournal : QuestActTemplate
    {
        public override bool Use(ICharacter character, Quest quest, int objective) // take reward
        {
            _log.Debug("QuestActConReportJournal");

            return true;
        }
    }
}
