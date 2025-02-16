using System;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActSupplyRemoveItem(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint ItemId { get; set; }

    /// <summary>
    /// Removes Count amount of Item
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), ItemId {ItemId}, Count {Count}");
        
        if (quest.Owner is Character player)
        {
            _ = player.Inventory.GetAllItemsByTemplate(new[] { SlotType.Bag }, ItemId, -1, out _, out var unitsCount);

            var toRemove = Math.Min(unitsCount, Count);
            var removed = player.Inventory.ConsumeItem(null, ItemTaskType.QuestRemoveSupplies, ItemId, toRemove, null);
            if (removed < Count)
                Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Did not have enough items to remove Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), ItemId {ItemId}, Count {removed}/{toRemove}(of {Count}) (found {unitsCount})");

            return true;
        }

        return false;
    }
}
