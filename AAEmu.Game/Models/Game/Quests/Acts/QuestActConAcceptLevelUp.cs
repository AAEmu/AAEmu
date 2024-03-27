using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConAcceptLevelUp(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public byte Level { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug("QuestActConAcceptLevelUp");

        return character.Level >= Level;
    }
}
