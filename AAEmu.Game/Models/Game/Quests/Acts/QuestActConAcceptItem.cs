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
        return quest.QuestAcceptorType == QuestAcceptorType.Item && quest.AcceptorId == ItemId && quest.Owner.Inventory.CheckItems(Items.SlotType.Inventory, ItemId, 1);
    }

    public override void QuestCleanup(Quest quest)
    {
        base.QuestCleanup(quest);
        if (!Cleanup)
            return;

        // quest_act_con_accept_items has no count column; RunAct always checks for 1. Leaving
        // Count at the QuestActTemplate default (0) made ConsumeItem a no-op and left the token in bag.
        quest.Owner?.Inventory.ConsumeItem(null, ItemTaskType.QuestRemoveSupplies, ItemId, ResolveConsumeAmount(Count), null);
    }

    public override void QuestDropped(Quest quest)
    {
        base.QuestDropped(quest);
        if (!DestroyWhenDrop)
            return;
        quest.Owner?.Inventory.ConsumeItem(null, ItemTaskType.QuestRemoveSupplies, ItemId, ResolveConsumeAmount(Count), null);
    }

    /// <summary>Accept-item rows have no count column; treat unset/non-positive as one token.</summary>
    public static int ResolveConsumeAmount(int count) => count > 0 ? count : 1;
}
