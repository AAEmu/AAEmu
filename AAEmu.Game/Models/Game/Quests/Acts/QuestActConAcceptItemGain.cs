using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Works the same as QuestActConAcceptItem, but allows Count and does not have any cleanup systems
/// </summary>
/// <param name="parentComponent"></param>
public class QuestActConAcceptItemGain(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint ItemId { get; set; }

    /// <summary>
    /// Checks if the Quest starter was indeed the provided Item and is in the inventory
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Trace($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), ItemId {ItemId}");
        return (quest.QuestAcceptorType == QuestAcceptorType.Item) && (quest.AcceptorId == ItemId) && quest.Owner.Inventory.CheckItems(Items.SlotType.Bag, ItemId, Count);
    }
}
