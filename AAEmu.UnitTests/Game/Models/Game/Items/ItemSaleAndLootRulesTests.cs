using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

/// <summary>
/// The sale limits an item template carries, and the loot behaviour auto_loot asks for.
/// </summary>
public class ItemSaleAndLootRulesTests
{
    private static ItemTemplate Sellable() => new() { Id = 1, Sellable = true };

    // --- sellable ---------------------------------------------------------------------------------

    [Test]
    public async Task AnUnsellableTemplateIsRefused()
    {
        var decision = ItemSaleRules.Evaluate(new ItemTemplate { Id = 1, Sellable = false }, 0);

        await Assert.That(decision.Allowed).IsFalse();
        await Assert.That(decision.Refusal).IsEqualTo(ItemSaleRefusal.NotSellable);
    }

    [Test]
    public async Task AMissingTemplateIsRefusedRatherThanSold()
    {
        await Assert.That(ItemSaleRules.Evaluate(null, 0).Allowed).IsFalse();
    }

    [Test]
    public async Task AnItemWithNoSaleColumnsSellsWithoutLimit()
    {
        var decision = ItemSaleRules.Evaluate(Sellable(), 0);

        await Assert.That(decision.Allowed).IsTrue();
        await Assert.That(decision.Remaining).IsEqualTo(ItemSaleRules.Unlimited);
        await Assert.That(ItemSaleRules.HasDailySaleLimit(Sellable())).IsFalse();
    }

    // --- auction_only -----------------------------------------------------------------------------

    [Test]
    public async Task AnAuctionOnlyItemIsRefusedByAVendorWhateverItsSaleColumnsSay()
    {
        var template = new ItemTemplate { Id = 2, Sellable = true, AuctionOnly = true };

        var decision = ItemSaleRules.Evaluate(template, 0);

        await Assert.That(decision.Allowed).IsFalse();
        await Assert.That(decision.Refusal).IsEqualTo(ItemSaleRefusal.AuctionOnly);
    }

    [Test]
    public async Task AnAuctionOnlyItemIsRefusedEvenWithAllowanceLeft()
    {
        var template = new ItemTemplate { Id = 3, Sellable = true, AuctionOnly = true, LimitedSaleCount = 10 };

        // The daily allowance is not a vendor permission: the item has no vendor market at all.
        await Assert.That(ItemSaleRules.Evaluate(template, 0).Refusal).IsEqualTo(ItemSaleRefusal.AuctionOnly);
    }

    [Test]
    public async Task AnUnsellableTemplateIsRefusedBeforeTheAuctionOnlyCheck()
    {
        var template = new ItemTemplate { Id = 4, Sellable = false, AuctionOnly = true };

        await Assert.That(ItemSaleRules.Evaluate(template, 0).Refusal).IsEqualTo(ItemSaleRefusal.NotSellable);
    }

    // --- one_time_sale ----------------------------------------------------------------------------

    [Test]
    public async Task AOneTimeItemSellsOnceAndThenNot()
    {
        var template = new ItemTemplate { Id = 5, Sellable = true, OneTimeSale = true };

        var first = ItemSaleRules.Evaluate(template, 0);
        var second = ItemSaleRules.Evaluate(template, 1);

        await Assert.That(first.Allowed).IsTrue();
        await Assert.That(second.Allowed).IsFalse();
        await Assert.That(second.Refusal).IsEqualTo(ItemSaleRefusal.OneTimeSaleExhausted);
    }

    [Test]
    public async Task OneTimeSaleWinsOverALimitedSaleCountOfZero()
    {
        // One shipped row sets one_time_sale with limited_sale_count 0, and 0 is what that column
        // means everywhere else. If one_time_sale did not win, that item would be sellable forever.
        var template = new ItemTemplate { Id = 6, Sellable = true, OneTimeSale = true, LimitedSaleCount = 0 };

        await Assert.That(ItemSaleRules.DailySaleLimit(template)).IsEqualTo(1);
        await Assert.That(ItemSaleRules.Evaluate(template, 1).Allowed).IsFalse();
    }

    [Test]
    public async Task OneTimeSaleWinsOverAWiderLimitedSaleCount()
    {
        var template = new ItemTemplate { Id = 7, Sellable = true, OneTimeSale = true, LimitedSaleCount = 10 };

        // The narrower of the two statements, not the wider one.
        await Assert.That(ItemSaleRules.DailySaleLimit(template)).IsEqualTo(1);
        await Assert.That(ItemSaleRules.Evaluate(template, 1).Refusal)
            .IsEqualTo(ItemSaleRefusal.OneTimeSaleExhausted);
    }

    // --- limited_sale_count -----------------------------------------------------------------------

    [Test]
    public async Task ALimitedItemSellsUpToItsCount()
    {
        var template = new ItemTemplate { Id = 8, Sellable = true, LimitedSaleCount = 3 };

        await Assert.That(ItemSaleRules.Evaluate(template, 0).Allowed).IsTrue();
        await Assert.That(ItemSaleRules.Evaluate(template, 1).Allowed).IsTrue();
        await Assert.That(ItemSaleRules.Evaluate(template, 2).Allowed).IsTrue();

        var exhausted = ItemSaleRules.Evaluate(template, 3);
        await Assert.That(exhausted.Allowed).IsFalse();
        await Assert.That(exhausted.Refusal).IsEqualTo(ItemSaleRefusal.LimitedSaleExhausted);
        await Assert.That(exhausted.Remaining).IsEqualTo(0);
    }

    [Test]
    public async Task ALimitedItemReportsHowManySalesAreLeft()
    {
        var template = new ItemTemplate { Id = 9, Sellable = true, LimitedSaleCount = 10 };

        // The shipped counts are 1, 3, 10, 14, 21, 28, 70 and 100.
        await Assert.That(ItemSaleRules.Evaluate(template, 0).Remaining).IsEqualTo(10);
        await Assert.That(ItemSaleRules.Evaluate(template, 4).Remaining).IsEqualTo(6);
    }

    [Test]
    public async Task ALimitedItemWithoutOneTimeSaleReportsTheLimitedRefusal()
    {
        var template = new ItemTemplate { Id = 10, Sellable = true, LimitedSaleCount = 1 };

        // The 119 shipped rows that set only limited_sale_count are told apart from the one-time ones
        // by which message they get, not by the behaviour.
        await Assert.That(ItemSaleRules.Evaluate(template, 1).Refusal)
            .IsEqualTo(ItemSaleRefusal.LimitedSaleExhausted);
    }

    [Test]
    public async Task ANegativeLimitedCountIsNoLimit()
    {
        var template = new ItemTemplate { Id = 11, Sellable = true, LimitedSaleCount = -3 };

        await Assert.That(ItemSaleRules.DailySaleLimit(template)).IsEqualTo(ItemSaleRules.Unlimited);
        await Assert.That(ItemSaleRules.Evaluate(template, 999).Allowed).IsTrue();
    }

    // --- auto_loot --------------------------------------------------------------------------------

    [Test]
    public async Task AnAutoLootItemIsNeverRolledFor()
    {
        var template = new ItemTemplate { Id = 12, AutoLoot = true };

        // Both of the reasons a roll exists - a grade floor and a bind-on-pickup - are about a prize.
        await Assert.That(ItemLootRules.IsAutoLoot(template)).IsTrue();
        await Assert.That(ItemLootRules.RequiresRoll(template, true, true)).IsFalse();
        await Assert.That(ItemLootRules.RequiresRoll(template, true, false)).IsFalse();
        await Assert.That(ItemLootRules.RequiresRoll(template, false, true)).IsFalse();
    }

    [Test]
    public async Task AnOrdinaryItemIsRolledForExactlyAsBefore()
    {
        var template = new ItemTemplate { Id = 13, AutoLoot = false };

        await Assert.That(ItemLootRules.RequiresRoll(template, false, false)).IsFalse();
        await Assert.That(ItemLootRules.RequiresRoll(template, true, false)).IsTrue();
        await Assert.That(ItemLootRules.RequiresRoll(template, false, true)).IsTrue();
    }

    [Test]
    public async Task AMissingTemplateIsNotAutoLoot()
    {
        await Assert.That(ItemLootRules.IsAutoLoot(null)).IsFalse();
        await Assert.That(ItemLootRules.RequiresRoll(null, true, true)).IsTrue();
    }

    // --- proc lifetime ----------------------------------------------------------------------------

    [Test]
    public async Task AProcWithoutALifetimeAlwaysFires()
    {
        var template = new ItemTemplate { Id = 14, ProcLifetime = 0 };

        await Assert.That(ItemProcLifetimeRules.HasLifetime(template)).IsFalse();
        await Assert.That(ItemProcLifetimeRules.CanFire(template, 0)).IsTrue();
        await Assert.That(ItemProcLifetimeRules.CanFire(template, 9999)).IsTrue();
        await Assert.That(ItemProcLifetimeRules.BindingMayAttach(template)).IsTrue();
    }

    [Test]
    public async Task ALifetimeBoundProcFiresExactlyAsOftenAsTheColumnAllows()
    {
        // The shipped values are 1 and 3.
        var once = new ItemTemplate { Id = 15, ProcLifetime = 1, ProcRechargeRestrictItemId = 45368 };
        var thrice = new ItemTemplate { Id = 16, ProcLifetime = 3, ProcRechargeRestrictItemId = 45368 };

        await Assert.That(ItemProcLifetimeRules.CanFire(once, 0)).IsTrue();
        await Assert.That(ItemProcLifetimeRules.CanFire(once, 1)).IsFalse();

        await Assert.That(ItemProcLifetimeRules.CanFire(thrice, 0)).IsTrue();
        await Assert.That(ItemProcLifetimeRules.CanFire(thrice, 2)).IsTrue();
        await Assert.That(ItemProcLifetimeRules.CanFire(thrice, 3)).IsFalse();
    }

    [Test]
    public async Task ASpentProcIsRechargeableOnlyWhenTheRowNamesAnItem()
    {
        var rechargeable = new ItemTemplate { Id = 17, ProcLifetime = 3, ProcRechargeRestrictItemId = 45368 };
        var spentForever = new ItemTemplate { Id = 18, ProcLifetime = 3 };

        await Assert.That(ItemProcLifetimeRules.IsRechargeable(rechargeable)).IsTrue();
        await Assert.That(ItemProcLifetimeRules.IsRechargeable(spentForever)).IsFalse();
        // A recharge item on a proc that never runs out is not a recharge, it is dead content.
        await Assert.That(ItemProcLifetimeRules.IsRechargeable(new ItemTemplate { Id = 19, ProcRechargeRestrictItemId = 1 })).IsFalse();
    }

    [Test]
    public async Task ALifetimeBoundBindingIsNotAttachedAndSaysWhy()
    {
        var lifetimeBound = new ItemTemplate { Id = 20, ProcLifetime = 1 };

        await Assert.That(ItemProcLifetimeRules.BindingMayAttach(lifetimeBound)).IsFalse();
        await Assert.That(ItemProcLifetimeRules.BindingMayAttach(null)).IsTrue();
    }
}
