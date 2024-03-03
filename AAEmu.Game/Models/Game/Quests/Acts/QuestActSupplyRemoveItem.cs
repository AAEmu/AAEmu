using System.Collections.Generic;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActSupplyRemoveItem : QuestActTemplate
{
    public uint ItemId { get; set; }

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Debug($"QuestActSupplyRemoveItem, ItemId: {ItemId}, Count: {Count}");

        if (character.Inventory.GetAllItemsByTemplate(new[] { SlotType.Inventory }, ItemId, -1, out var foundItems, out var unitsCount))
        {
            return false;
        }

        var count = Count <= unitsCount ? Count : unitsCount;
        for (var i = 0; i < count; i++)
        {
            character.Inventory.Bag.RemoveItem(ItemTaskType.QuestRemoveSupplies, foundItems[i], true);
        }

        return true;
    }
}
