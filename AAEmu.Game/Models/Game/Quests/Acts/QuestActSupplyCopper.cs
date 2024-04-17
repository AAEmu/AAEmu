using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActSupplyCopper(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public int Amount { get; set; }

    /// <summary>
    /// Gives copper coins
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), Amount {Amount}");
        quest.QuestRewardCoinsPool += Amount;
        return true;
    }
}
