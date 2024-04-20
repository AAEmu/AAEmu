using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Not used, Gives AAPoints to the player
/// </summary>
/// <param name="parentComponent"></param>
public class QuestActSupplyAaPoint(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public int Point { get; set; }

    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        return base.RunAct(quest, questAct, currentObjectiveCount);
    }
}
