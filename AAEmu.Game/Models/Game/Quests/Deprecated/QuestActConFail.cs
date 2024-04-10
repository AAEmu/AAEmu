using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Not sure how this was supposed to work. Does not seem to be used anymore
/// </summary>
/// <param name="parentComponent"></param>
public class QuestActConFail(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public bool ForceChangeComponent { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        // TODO: Implement ForceChangeComponent
        Logger.Debug("QuestActConFail");
        return false;
    }
}
