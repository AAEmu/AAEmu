using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

/// <summary>
/// Vendor-sell wire layout and buy-back list capacity.
/// </summary>
public class VendorSellWireRulesTests
{
    [Test]
    public async Task EntryWireSize_MatchesClientSellEntrySerializer()
    {
        // type + index + id + stack + reservation + type + dbSlaveId + type
        await Assert.That(VendorSellWireRules.EntryWireSize).IsEqualTo(34);
    }

    [Test]
    public async Task IsSellableContainer_AcceptsBagAndEquipmentOnly()
    {
        await Assert.That(VendorSellWireRules.IsSellableContainer(SlotType.Inventory)).IsTrue();
        await Assert.That(VendorSellWireRules.IsSellableContainer(SlotType.Equipment)).IsTrue();
        await Assert.That(VendorSellWireRules.IsSellableContainer(SlotType.Bank)).IsFalse();
        await Assert.That(VendorSellWireRules.IsSellableContainer(SlotType.None)).IsFalse();
    }

    /// <summary>
    /// The sell builder leaves stack at 0; quantity for payout is the bag count, not this field.
    /// </summary>
    [Test]
    public async Task StackFieldAllowsSale_ZeroWireStack_UsesBagCount()
    {
        await Assert.That(VendorSellWireRules.StackFieldAllowsSale(0, 1)).IsTrue();
        await Assert.That(VendorSellWireRules.StackFieldAllowsSale(0, 50)).IsTrue();
        await Assert.That(VendorSellWireRules.StackFieldAllowsSale(0, 0)).IsFalse();
    }

    [Test]
    public async Task StackFieldAllowsSale_PositiveWireStack_MustEqualBagCount()
    {
        await Assert.That(VendorSellWireRules.StackFieldAllowsSale(1, 1)).IsTrue();
        await Assert.That(VendorSellWireRules.StackFieldAllowsSale(50, 50)).IsTrue();
        await Assert.That(VendorSellWireRules.StackFieldAllowsSale(1, 50)).IsFalse();
        await Assert.That(VendorSellWireRules.StackFieldAllowsSale(50, 1)).IsFalse();
    }

    [Test]
    public async Task ExcessBuybackCount_KeepsTheSoldItemListCap()
    {
        await Assert.That(VendorSellWireRules.ExcessBuybackCount(0)).IsEqualTo(0);
        await Assert.That(VendorSellWireRules.ExcessBuybackCount(12)).IsEqualTo(0);
        await Assert.That(VendorSellWireRules.ExcessBuybackCount(13)).IsEqualTo(1);
        await Assert.That(VendorSellWireRules.ExcessBuybackCount(20)).IsEqualTo(8);
    }

    /// <summary>
    /// Regression for the silent-sell bug: reading a padded 4-byte slot when the wire only carries
    /// type+index shifts the instance id, so GetItemBySlot never matches and the bag stays put.
    /// </summary>
    [Test]
    public async Task SellEntryLayout_DoesNotIncludeSlotPaddingBytes()
    {
        const int paddedSlotMistake = 2;
        await Assert.That(VendorSellWireRules.EntryWireSize + paddedSlotMistake).IsNotEqualTo(34);
        await Assert.That(VendorSellWireRules.EntryWireSize).IsEqualTo(1 + 1 + 8 + 4 + 8 + 4 + 4 + 4);
    }

    /// <summary>
    /// Two back-to-back entries through the production reader. A padded u32 slot would steal two
    /// bytes from the first id and shift every field of the second entry.
    /// </summary>
    [Test]
    public async Task ReadEntry_TwoEntries_KeepsSlotAndIdBoundaries()
    {
        var body = new PacketStream();
        WriteSellEntry(body, SlotType.Inventory, slot: 3, itemId: 0x1111_2222_3333_4444UL, stack: 0);
        WriteSellEntry(body, SlotType.Equipment, slot: 7, itemId: 0xAAAA_BBBB_CCCC_DDDDUL, stack: 50);
        var stream = new PacketStream(body);

        var first = VendorSellWireRules.ReadEntry(stream);
        var second = VendorSellWireRules.ReadEntry(stream);

        await Assert.That(first.SlotType).IsEqualTo(SlotType.Inventory);
        await Assert.That(first.Slot).IsEqualTo((byte)3);
        await Assert.That(first.ItemId).IsEqualTo(0x1111_2222_3333_4444UL);
        await Assert.That(first.Stack).IsEqualTo(0u);

        await Assert.That(second.SlotType).IsEqualTo(SlotType.Equipment);
        await Assert.That(second.Slot).IsEqualTo((byte)7);
        await Assert.That(second.ItemId).IsEqualTo(0xAAAA_BBBB_CCCC_DDDDUL);
        await Assert.That(second.Stack).IsEqualTo(50u);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    private static void WriteSellEntry(PacketStream stream, SlotType type, byte slot, ulong itemId, uint stack)
    {
        stream.Write((byte)type);
        stream.Write(slot);
        stream.Write(itemId);
        stream.Write(stack);
        stream.Write(0UL); // removeReservationTime
        stream.Write(0u);  // detailType
        stream.Write(0u);  // dbSlaveId
        stream.Write(0u);  // ownerType
    }
}
