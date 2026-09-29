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
        var decision = ItemSaleRules.Evaluate(new ItemTemplate { Id = 1, Sellable = false });

        await Assert.That(decision.Allowed).IsFalse();
        await Assert.That(decision.Refusal).IsEqualTo(ItemSaleRefusal.NotSellable);
    }

    [Test]
    public async Task AMissingTemplateIsRefusedRatherThanSold()
    {
        await Assert.That(ItemSaleRules.Evaluate(null).Allowed).IsFalse();
    }

    /// <summary>
    /// A sellable item with no other vendor column set is simply sellable. There is no allowance to
    /// report and no counter to consult, because the vendor path does not read the purchase limits.
    /// </summary>
    [Test]
    public async Task ASellableItemIsAllowedWithNoFurtherQuestions()
    {
        var decision = ItemSaleRules.Evaluate(Sellable());

        await Assert.That(decision.Allowed).IsTrue();
        await Assert.That(decision.Refusal).IsEqualTo(ItemSaleRefusal.None);
    }


    // --- auction_only -----------------------------------------------------------------------------

    [Test]
    public async Task AnAuctionOnlyItemIsRefusedByAVendorWhateverItsSaleColumnsSay()
    {
        var template = new ItemTemplate { Id = 2, Sellable = true, AuctionOnly = true };

        var decision = ItemSaleRules.Evaluate(template);

        await Assert.That(decision.Allowed).IsFalse();
        await Assert.That(decision.Refusal).IsEqualTo(ItemSaleRefusal.AuctionOnly);
    }

    [Test]
    public async Task AnAuctionOnlyItemIsRefusedEvenWithAllowanceLeft()
    {
        var template = new ItemTemplate { Id = 3, Sellable = true, AuctionOnly = true, LimitedSaleCount = 10 };

        // The daily allowance is not a vendor permission: the item has no vendor market at all.
        await Assert.That(ItemSaleRules.Evaluate(template).Refusal).IsEqualTo(ItemSaleRefusal.AuctionOnly);
    }

    [Test]
    public async Task AnUnsellableTemplateIsRefusedBeforeTheAuctionOnlyCheck()
    {
        var template = new ItemTemplate { Id = 4, Sellable = false, AuctionOnly = true };

        await Assert.That(ItemSaleRules.Evaluate(template).Refusal).IsEqualTo(ItemSaleRefusal.NotSellable);
    }

    // --- one_time_sale / limited_sale_count are not vendor-sale columns ---------------------------

    /// <summary>
    /// The regression this section pins: those two columns are purchase limits, and reading them as
    /// a vendor-sale allowance was wrong on the content's own terms. Every one of the twelve shipped
    /// rows that sets one_time_sale has sellable unset, so the item could never reach a vendor at all,
    /// and of the 130 rows with a positive limited_sale_count, 118 likewise cannot be sold. Their
    /// values - 1, 3, 10, 14, 21, 28, 70, 100, on blueprints, boxes and exchange tickets - are how
    /// many may be bought, which is a different question with a different owner.
    /// </summary>
    [Test]
    public async Task AOneTimeItemIsNotRefusedByAVendorForASaleLimit()
    {
        var template = new ItemTemplate { Id = 3, Sellable = true, OneTimeSale = true };

        var decision = ItemSaleRules.Evaluate(template);

        await Assert.That(decision.Allowed).IsTrue();
        await Assert.That(decision.Refusal).IsEqualTo(ItemSaleRefusal.None);
    }

    [Test]
    public async Task ALimitedPurchaseItemIsNotRefusedByAVendorForALimitOfOne()
    {
        // limited_sale_count 1 on a sellable item is exactly the shape that used to mean "one sale a
        // day, for the whole server". A vendor must not answer to it.
        var template = new ItemTemplate { Id = 4, Sellable = true, LimitedSaleCount = 1 };

        var decision = ItemSaleRules.Evaluate(template);

        await Assert.That(decision.Allowed).IsTrue();
    }

    /// <summary>
    /// A vendor sale of a limited item is repeatable, because nothing about the vendor path counts
    /// them. Swept rather than a single call, so a rule that quietly started counting could not pass.
    /// </summary>
    [Test]
    public async Task ARepeatedVendorSaleOfALimitedItemIsNotRefused()
    {
        var template = new ItemTemplate { Id = 5, Sellable = true, LimitedSaleCount = 1 };

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var decision = ItemSaleRules.Evaluate(template);

            await Assert.That(decision.Allowed).IsTrue();
        }
    }

    /// <summary>
    /// The two vendor columns still decide, and they still win in the same order: auction_only beats
    /// a sellable item outright, and an unsellable item is refused before either is consulted.
    /// </summary>
    [Test]
    public async Task TheVendorColumnsStillRefuseTheItemsTheyName()
    {
        var unsellable = ItemSaleRules.Evaluate(new ItemTemplate { Id = 6, Sellable = false });
        var auctionOnly = ItemSaleRules.Evaluate(
            new ItemTemplate { Id = 7, Sellable = true, AuctionOnly = true });
        var allowed = ItemSaleRules.Evaluate(new ItemTemplate { Id = 8, Sellable = true });

        await Assert.That(unsellable.Refusal).IsEqualTo(ItemSaleRefusal.NotSellable);
        await Assert.That(auctionOnly.Refusal).IsEqualTo(ItemSaleRefusal.AuctionOnly);
        await Assert.That(allowed.Allowed).IsTrue();
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
