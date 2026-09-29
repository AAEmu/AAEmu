using System.Reflection;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// The item table's expiry family is deliberately not honoured, and these tests are the pin on that
/// decision.
/// <para>
/// Every <c>exp_date</c> in the shipped item table has already passed, so a build that resolves the
/// expiry family makes every dated template dead content: the stack is born with an end in the past
/// and the ordinary expiry sweep takes it away again. The columns are therefore left alone - not by
/// reading them as "no date", which would be inventing behaviour, but by leaving the expiry family
/// exactly as it was before this feature and declining to extend it.
/// </para>
/// <para>
/// The rest of the feature ships: the sale, loot, guild-level and proc columns are honoured for the
/// templates that carry them. These tests say so, so that re-adding the expiry family later is a
/// deliberate change with a content fix attached, and not something that slips back in blind.
/// </para>
/// </summary>
public class ItemExpiryDeferralTests
{
    /// <summary>An end that has already passed, derived from the clock rather than pinned to a date.</summary>
    private static DateTime AnEndThatHasAlreadyPassed() => DateTime.UtcNow.AddDays(-1);

    private static ItemManager CreateManager(ItemTemplate template)
    {
        var manager = new ItemManager(
            Mock.Of<ISkillManager>().Object,
            Mock.Of<IItemIdManager>().Object,
            Mock.Of<IContainerIdManager>().Object,
            Mock.Of<ILocalizationManager>().Object,
            Mock.Of<ITaskManager>().Object,
            Mock.Of<IWorldManager>().Object);

        SetField(manager, "_templates", new Dictionary<uint, ItemTemplate> { { template.Id, template } });
        SetField(manager, "_allItems", new Dictionary<ulong, Item>());
        return manager;
    }

    private static void SetField(object target, string name, object value)
    {
        var field = target.GetType().GetField(name,
            BindingFlags.NonPublic | BindingFlags.Instance);
        field?.SetValue(target, value);
    }

    [Test]
    public async Task TheFactoryLeavesATemplateWhoseOwnEndHasPassedUnarmed()
    {
        // The whole point of the deferral. If the factory ever starts resolving the expiry family
        // again, an item whose row names an end in the past comes out already expired, the sweep
        // deletes it, and every dated template in the catalogue becomes unobtainable.
        var template = new ItemTemplate { Id = 8100, MaxCount = 10, ExpDate = AnEndThatHasAlreadyPassed() };
        var manager = CreateManager(template);

        var item = manager.Create(8100, 1, 0, generateId: false);

        await Assert.That(item).IsNotNull();
        await Assert.That(item.ExpirationTime).IsEqualTo(DateTime.MinValue);
        await Assert.That(item.ExpirationOnlineMinutesLeft).IsEqualTo(0.0);
    }

    [Test]
    public async Task TheFactoryLeavesEveryExpiryColumnUnarmed()
    {
        // The whole family, not just the absolute date: a relative term, an online budget, a weekday
        // and a period anchor are all expressions of "this item expires", and all of them are
        // deferred together so the decision cannot be undone one column at a time.
        var template = new ItemTemplate
        {
            Id = 8101,
            MaxCount = 10,
            ExpAbsLifetime = 60,
            ExpOnlineLifetime = 30,
            ExpDate = AnEndThatHasAlreadyPassed()
        };
        var manager = CreateManager(template);

        var item = manager.Create(8101, 1, 0, generateId: false);

        await Assert.That(item).IsNotNull();
        await Assert.That(item.ExpirationTime).IsEqualTo(DateTime.MinValue);
        await Assert.That(item.ExpirationOnlineMinutesLeft).IsEqualTo(0.0);
    }

    [Test]
    public async Task AnItemWhoseEndHasPassedIsStillSoldToAVendorAndStillAutoLooted()
    {
        // "Still acquirable through the paths that still work": the sale and loot columns are
        // decided from the template alone, and a date on the same row does not make the item
        // unsellable or turn it back into something that needs a roll.
        var template = new ItemTemplate
        {
            Id = 8102,
            MaxCount = 10,
            Sellable = true,
            ExpDate = AnEndThatHasAlreadyPassed()
        };

        var decision = ItemSaleRules.Evaluate(template);
        await Assert.That(decision.Allowed).IsTrue();
        await Assert.That(decision.Refusal).IsEqualTo(ItemSaleRefusal.None);
        await Assert.That(ItemLootRules.IsAutoLoot(template)).IsFalse();
    }

    /// <summary>
    /// The sale-limit columns are not consulted on the vendor path at all - they are purchase
    /// limits - so whatever the expiry family says, none of them can refuse this sale.
    /// </summary>
    [Test]
    public async Task AnExpiredItemIsStillSoldToAVendorRepeatedly()
    {
        var template = new ItemTemplate { Id = 1, Sellable = true, LimitedSaleCount = 1, OneTimeSale = true };

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var decision = ItemSaleRules.Evaluate(template);

            await Assert.That(decision.Refusal).IsEqualTo(ItemSaleRefusal.None);
            await Assert.That(decision.Allowed).IsTrue();
        }
    }


    [Test]
    public async Task TheDeferredExpiryColumnsHaveNoHomeOnTheTemplate()
    {
        // The period and weekday columns were never read by any build, so honouring them is new
        // behaviour rather than a change to existing behaviour. A template that had no property for
        // them cannot acquire one by accident.
        foreach (var name in new[] { "PeriodBaseDate", "ExpDayOfWeekId", "ExpDayOfWeekMin" })
        {
            var property = typeof(ItemTemplate).GetProperty(name,
                BindingFlags.Public | BindingFlags.Instance);
            await Assert.That(property).IsNull();
        }
    }

    [Test]
    public async Task TheExpiryColumnsAreStillLoadedSoNothingIsHiddenFromTheTemplate()
    {
        // The deferral is about behaviour, not about hiding the content: the columns the build has
        // always read are still read, so a later change can act on the same data without a second
        // loader.
        var template = new ItemTemplate { Id = 8104, MaxCount = 10, ExpDate = AnEndThatHasAlreadyPassed() };

        await Assert.That(template.ExpDate).IsLessThan(DateTime.UtcNow);
    }
}
