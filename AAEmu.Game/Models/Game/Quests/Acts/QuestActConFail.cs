using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConFail(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public bool ForceChangeComponent { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        // TODO: Implement ForceChangeComponent
        Logger.Debug("QuestActConFail");
        return false;
    }

    /// <summary>
    /// Not sure how this was supposed to work. Does not seem to be used anymore
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, int currentObjectiveCount)
    {
        Logger.Debug($"QuestActConFail({DetailId}).RunAct: Quest: {quest.TemplateId}, ForceChangeComponent {ForceChangeComponent}");
        return false;
    }
}
