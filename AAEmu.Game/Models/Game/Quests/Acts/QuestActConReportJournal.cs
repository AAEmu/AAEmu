using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConReportJournal : QuestActTemplate
{
    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective) // take reward
    {
        Logger.Debug("QuestActConReportJournal");

        return true;
    }
}
