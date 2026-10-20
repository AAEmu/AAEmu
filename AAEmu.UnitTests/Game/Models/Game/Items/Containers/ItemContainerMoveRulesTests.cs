using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;

namespace AAEmu.UnitTests.Game.Models.Game.Items.Containers;

public class ItemContainerMoveRulesTests
{
    [Test]
    [Arguments(SlotType.Equipment, SlotType.Inventory, true)]
    [Arguments(SlotType.Inventory, SlotType.Equipment, false)]
    [Arguments(SlotType.Inventory, SlotType.Inventory, false)]
    [Arguments(SlotType.Equipment, SlotType.Bank, false)]
    [Arguments(SlotType.Equipment, SlotType.Mail, false)]
    [Arguments(SlotType.None, SlotType.Inventory, false)]
    public async Task UsesSwapSlot_OnlyForEquipmentIntoInventory(SlotType from, SlotType to, bool expected)
    {
        await Assert.That(ItemContainerMoveRules.UsesSwapSlot(from, to)).IsEqualTo(expected);
    }
}
