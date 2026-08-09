using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Quest start gate used by UI-accepted quests (daily schedule / Path of Destiny).
/// </summary>
public class QuestActConAcceptUi(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Trace(
            "{0}({1}).RunAct: Quest: {2}, Owner {3} ({4})",
            QuestActTemplateName, DetailId, quest.TemplateId, quest.Owner.Name, quest.Owner.Id);
        return true;
    }
}
