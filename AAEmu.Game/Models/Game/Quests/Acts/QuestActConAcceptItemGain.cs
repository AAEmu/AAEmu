using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
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

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug($"QuestActConAcceptItemGain: ItemId {ItemId}, Count {Count}");
        return objective >= Count;
    }

    /// <summary>
    /// Checks if the Quest starter was indeed the provided Item and is in the inventory
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, int currentObjectiveCount)
    {
        Logger.Trace($"QuestActConAcceptItemGain({DetailId}).RunAct: Quest: {quest.TemplateId}, ItemId {ItemId}");
        return (quest.QuestAcceptorType == QuestAcceptorType.Item) && (quest.AcceptorId == ItemId) && quest.Owner.Inventory.CheckItems(Items.SlotType.Inventory, ItemId, Count);
    }
}
