using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActSupplyItem(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent), IQuestActGenericItem
{
    public uint ItemId { get; set; }
    public byte GradeId { get; set; }
    public bool ShowActionBar { get; set; }
    public bool Cleanup { get; set; }
    public bool DropWhenDestroy { get; set; }
    public bool DestroyWhenDrop { get; set; }

    /// <summary>
    /// Gives item to the player, either directly equipped, bag or by mail (for non-backpack)
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns>False if it failed to provide the item</returns>
    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), ItemId {ItemId}, GradeId {GradeId}, Count {Count}, ShowActionBar {ShowActionBar}, Cleanup {Cleanup}, DropWhenDestroy {DropWhenDestroy}, DestroyWhenDrop {DestroyWhenDrop}");

        var toAddCount = Count;

        if (ParentComponent.KindId < QuestComponentKind.Reward && quest.Owner.Inventory.GetAllItemsByTemplate(null, ItemId, -1, out _, out var foundCount))
            toAddCount -= foundCount;

        if (toAddCount < 0)
            return true;

        if (quest.Owner is Character player)
        {
            // If a backpack, directly handle it, otherwise use the reward pool
            if (ItemManager.Instance.IsAutoEquipTradePack(ItemId))
            {
                return player.Inventory.TryEquipNewBackPack(ItemTaskType.QuestSupplyItems, ItemId, toAddCount, GradeId);
            }

            // Add item to reward pool (pool gets distributed and reset at the end of each step)
            quest.QuestRewardItemsPool.Add(new ItemCreationDefinition(ItemId, toAddCount, GradeId));
            return true;
        }
        return false; // Not a player somehow, should never get here
    }

    public override void QuestCleanup(Quest quest)
    {
        base.QuestCleanup(quest);
        if (!Cleanup || ParentComponent.KindId == QuestComponentKind.Reward)
            return;

        quest.Owner?.Inventory.ConsumeItem(null, ItemTaskType.QuestRemoveSupplies, ItemId, Count, null);
    }

    public override void QuestDropped(Quest quest)
    {
        base.QuestDropped(quest);
        if (!DestroyWhenDrop || ParentComponent.KindId == QuestComponentKind.Reward)
            return;

        quest.Owner?.Inventory.ConsumeItem(null, ItemTaskType.QuestRemoveSupplies, ItemId, Count, null);
    }

}
