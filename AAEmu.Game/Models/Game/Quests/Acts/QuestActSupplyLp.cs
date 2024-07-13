using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Hardly any quest uses this as a reward, and only 1 LP (probably to reimburse the used labor for that quest) 
/// </summary>
/// <param name="parentComponent"></param>
public class QuestActSupplyLp(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public int LaborPower { get; set; }

    /// <summary>
    /// Adds labor
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), LaborPower {LaborPower}");
        if (quest.Owner is Character player)
            player.ChangeLabor((short)LaborPower, 0);
        return true;
    }
}
