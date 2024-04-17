using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActCheckCompleteComponent(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint CompleteComponent { get; set; }

    /// <summary>
    /// Checks if a specific component of this quest has its objective completed
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), Complete Component {CompleteComponent}");
        if (questAct.QuestComponent.Parent.Components.TryGetValue(CompleteComponent, out var targetComponent))
        {
            // Found target component, check if it's completed
            // Basically the Same as doing a RunAct on all acts of this component to check the results
            return targetComponent.RunComponent();
        }
        else
        {
            Logger.Error($"{QuestActTemplateName}({DetailId}).RunAct: Quest {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), Complete Component {CompleteComponent} NOT FOUND!");
        }
        return false;
    }
}
