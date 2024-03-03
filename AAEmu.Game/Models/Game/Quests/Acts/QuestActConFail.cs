using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConFail : QuestActTemplate
{
    public bool ForceChangeComponent { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        // TODO: Implement ForceChangeComponent
        Logger.Debug("QuestActConFail");
        return false;
    }
}
