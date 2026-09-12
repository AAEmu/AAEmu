using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

public sealed class CSDestroyItemPacketTests
{
    [Test]
    public async Task Validation_RejectsDeclaredAndResolvedSystemStorage()
    {
        var bag = new ItemContainer(10, SlotType.Inventory, false, null);
        var system = new ItemContainer(11, SlotType.System, false, null);
        var item = new Item(9001, new ItemTemplate { Id = 123 }, 1)
        {
            SlotType = SlotType.Inventory,
            Slot = 4,
            _holdingContainer = bag
        };

        await Assert.That(CSDestroyItemPacket.IsValidDestroyTarget(
            item, item.Id, SlotType.System, 1)).IsFalse();

        item.SlotType = SlotType.System;
        await Assert.That(CSDestroyItemPacket.IsValidDestroyTarget(
            item, item.Id, SlotType.Inventory, 1)).IsFalse();

        item.SlotType = SlotType.Inventory;
        item._holdingContainer = system;
        await Assert.That(CSDestroyItemPacket.IsValidDestroyTarget(
            item, item.Id, SlotType.Inventory, 1)).IsFalse();

        item._holdingContainer = bag;
        await Assert.That(CSDestroyItemPacket.IsValidDestroyTarget(
            item, item.Id, SlotType.Inventory, 1)).IsTrue();
        await Assert.That(item.Count).IsEqualTo(1);
    }
}
