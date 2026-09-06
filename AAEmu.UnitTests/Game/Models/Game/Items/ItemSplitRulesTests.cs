using AAEmu.Game.Models.Game.Auction;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

public class ItemSplitRulesTests
{
    [Test]
    public async Task IsSplitAmount_RejectsZeroFullAndOverdraw()
    {
        await Assert.That(ItemSplitRules.IsSplitAmount(101, 1)).IsTrue();
        await Assert.That(ItemSplitRules.IsSplitAmount(101, 100)).IsTrue();
        await Assert.That(ItemSplitRules.IsSplitAmount(101, 0)).IsFalse();
        await Assert.That(ItemSplitRules.IsSplitAmount(101, 101)).IsFalse();
        await Assert.That(ItemSplitRules.IsSplitAmount(101, 102)).IsFalse();
        await Assert.That(ItemSplitRules.IsSplitAmount(1, 1)).IsFalse();
        await Assert.That(ItemSplitRules.IsSplitAmount(0, 1)).IsFalse();
    }

    [Test]
    public async Task ConservesCount_101Split1_Is100And1()
    {
        await Assert.That(ItemSplitRules.ConservesCount(101, 1, 100, 1)).IsTrue();
        await Assert.That(ItemSplitRules.ConservesCount(101, 1, 101, 1)).IsFalse();
        await Assert.That(ItemSplitRules.ConservesCount(101, 1, 100, 101)).IsFalse();
        await Assert.That(ItemSplitRules.ConservesCount(101, 1, 99, 1)).IsFalse();
    }

    [Test]
    public async Task PlaceNewStack_AssignsTheSourceOwner()
    {
        var dest = new Item(9, new ItemTemplate { Id = 29656 }, 1);
        ItemSplitRules.PlaceNewStack(dest, 39, SlotType.Inventory, 4);

        await Assert.That(dest.OwnerId).IsEqualTo(39u);
        await Assert.That(dest.SlotType).IsEqualTo(SlotType.Inventory);
        await Assert.That(dest.Slot).IsEqualTo(4);
        await Assert.That(AuctionHouseRules.IsOwnedInBag(dest, 39)).IsTrue();
        await Assert.That(AuctionHouseRules.IsOwnedInBag(dest, 0)).IsFalse();
    }

    [Test]
    public async Task CopyStackFields_KeepsSoulBoundSoSplitCannotUnbind()
    {
        var source = new Item(1, new ItemTemplate { Id = 29656, Sellable = true }, 101);
        source.SetFlag(ItemFlag.SoulBound);
        source.ChargeCount = 3;
        var dest = new Item(2, new ItemTemplate { Id = 29656, Sellable = true }, 1);

        ItemSplitRules.CopyStackFields(source, dest);

        await Assert.That(dest.HasFlag(ItemFlag.SoulBound)).IsTrue();
        await Assert.That(dest.ChargeCount).IsEqualTo(3);
        await Assert.That(AuctionHouseRules.IsListableItem(dest)).IsFalse();
    }

    [Test]
    public async Task ReportTaskType_SplitUsesTheSplitReason()
    {
        await Assert.That(ItemSplitRules.ReportTaskType(true, ItemTaskType.SwapItems))
            .IsEqualTo(ItemTaskType.Split);
        await Assert.That(ItemSplitRules.ReportTaskType(true, ItemTaskType.Split))
            .IsEqualTo(ItemTaskType.Split);
        await Assert.That(ItemSplitRules.ReportTaskType(false, ItemTaskType.Split))
            .IsEqualTo(ItemTaskType.SwapItems);
        await Assert.That(ItemSplitRules.ReportTaskType(false, ItemTaskType.SwapItems))
            .IsEqualTo(ItemTaskType.SwapItems);
    }
}
