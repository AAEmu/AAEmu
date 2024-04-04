using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConAcceptItem(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent), IQuestActGenericItem
{
    public uint ItemId { get; set; }
    public bool Cleanup { get; set; }
    public bool DropWhenDestroy { get; set; }
    public bool DestroyWhenDrop { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective) // triggered by using things
    {
        Logger.Debug($"QuestActConAcceptItem: ItemId {ItemId}");

        quest.QuestAcceptorType = QuestAcceptorType.Item;
        quest.AcceptorId = ItemId;

        return character.Inventory.CheckItems(Items.SlotType.Inventory, ItemId, 1);
    }

    /// <summary>
    /// Checks if the Quest starter was indeed the provided Item and is in the inventory
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, int currentObjectiveCount)
    {
        Logger.Trace($"QuestActConAcceptItem({DetailId}).RunAct: Quest: {quest.TemplateId}, ItemId {ItemId}");
        return (quest.QuestAcceptorType == QuestAcceptorType.Item) && (quest.AcceptorId == ItemId) && quest.Owner.Inventory.CheckItems(Items.SlotType.Inventory, ItemId, 1);
    }

    public override void QuestCleanup(Quest quest)
    {
        base.QuestCleanup(quest);
        if (!Cleanup)
            return;
        quest.Owner?.Inventory.ConsumeItem([], ItemTaskType.QuestRemoveSupplies, ItemId, 1, null);
    }

    public override void QuestDropped(Quest quest)
    {
        base.QuestDropped(quest);
        if (!DestroyWhenDrop)
            return;
        quest.Owner?.Inventory.ConsumeItem([], ItemTaskType.QuestRemoveSupplies, ItemId, 1, null);
    }
}
