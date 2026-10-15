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
}
