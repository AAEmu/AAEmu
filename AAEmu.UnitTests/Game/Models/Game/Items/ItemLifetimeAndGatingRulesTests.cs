using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

/// <summary>
/// The lifespan a freshly created item inherits from its template row, and the guild-level
/// floor that gates an item's use.
/// </summary>
public class ItemLifetimeAndGatingRulesTests
{
    private static readonly DateTime Now = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    private static Item NewItem(ItemTemplate template)
    {
        var item = new Item(4242, template, 1) { Id = 4242, TemplateId = template.Id, CreateTime = Now };
        return item;
    }

    // --- lifespan: exp_abs_lifetime ---------------------------------------------------------------

    [Test]
    public async Task ExpAbsLifetime_ArmsExpirationFromCreationInstant()
    {
        var item = NewItem(new ItemTemplate { Id = 1, ExpAbsLifetime = 1440 });

        var armed = ItemLifetimeRules.ApplyNewItemLifespan(item, Now);

        await Assert.That(armed).IsTrue();
        await Assert.That(item.ExpirationTime).IsEqualTo(Now.AddMinutes(1440));
        await Assert.That(item.ExpirationOnlineMinutesLeft).IsEqualTo(0.0);
    }

    [Test]
    public async Task NoLifespanColumns_LeavesItemNeverExpiring()
    {
        var item = NewItem(new ItemTemplate { Id = 2 });

        var armed = ItemLifetimeRules.ApplyNewItemLifespan(item, Now);

        await Assert.That(armed).IsFalse();
        await Assert.That(item.ExpirationTime).IsEqualTo(DateTime.MinValue);
        await Assert.That(item.ExpirationOnlineMinutesLeft).IsEqualTo(0.0);
    }

    [Test]
    public async Task ExpDate_AndTheRelativeColumnEndTheItemAtTheSoonerOfTheTwo()
    {
        var absolute = Now.AddHours(2);
        var item = NewItem(new ItemTemplate { Id = 3, ExpAbsLifetime = 1440, ExpDate = absolute });

        ItemLifetimeRules.ApplyNewItemLifespan(item, Now);

        // 44 shipped rows set both columns. The end is whichever of the two statements is soonest;
        // an absolute date always outranking the relative one would have made every one of those
        // rows produce an item that is born already expired.
        await Assert.That(item.ExpirationTime).IsEqualTo(absolute);
        await Assert.That(item.ExpirationTime).IsLessThan(Now.AddMinutes(1440));
    }

    [Test]
    public async Task TheRelativeColumnEndsTheItemWhenItIsTheSoonerStatement()
    {
        var item = NewItem(new ItemTemplate { Id = 3, ExpAbsLifetime = 1440, ExpDate = Now.AddDays(40) });

        ItemLifetimeRules.ApplyNewItemLifespan(item, Now);

        await Assert.That(item.ExpirationTime).IsEqualTo(Now.AddMinutes(1440));
    }

    [Test]
    public async Task ExpOnlineLifetime_TrackedSeparatelyFromAbsoluteEnd()
    {
        var item = NewItem(new ItemTemplate { Id = 4, ExpOnlineLifetime = 30 });

        var armed = ItemLifetimeRules.ApplyNewItemLifespan(item, Now);

        await Assert.That(armed).IsTrue();
        await Assert.That(item.ExpirationOnlineMinutesLeft).IsEqualTo(30.0);
        // The online budget must not fabricate an absolute end of its own.
        await Assert.That(item.ExpirationTime).IsEqualTo(DateTime.MinValue);
    }

    [Test]
    public async Task BothLifetimes_ArmIndependently()
    {
        var item = NewItem(new ItemTemplate { Id = 5, ExpAbsLifetime = 60, ExpOnlineLifetime = 15 });

        ItemLifetimeRules.ApplyNewItemLifespan(item, Now);

        await Assert.That(item.ExpirationTime).IsEqualTo(Now.AddMinutes(60));
        await Assert.That(item.ExpirationOnlineMinutesLeft).IsEqualTo(15.0);
    }

    [Test]
    public async Task NegativeLifetimeColumn_DoesNotArmATimer()
    {
        var item = NewItem(new ItemTemplate { Id = 6, ExpAbsLifetime = -5, ExpOnlineLifetime = -1 });

        var armed = ItemLifetimeRules.ApplyNewItemLifespan(item, Now);

        await Assert.That(armed).IsFalse();
        await Assert.That(item.ExpirationTime).IsEqualTo(DateTime.MinValue);
        await Assert.That(item.ExpirationOnlineMinutesLeft).IsEqualTo(0.0);
    }

    [Test]
    public async Task MissingTemplate_IsNotArmed()
    {
        var item = new Item(7, new ItemTemplate { Id = 7 }, 1) { Template = null };

        var armed = ItemLifetimeRules.ApplyNewItemLifespan(item, Now);

        await Assert.That(armed).IsFalse();
        await Assert.That(item.ExpirationTime).IsEqualTo(DateTime.MinValue);
    }

    // --- lifespan: the expiry tick consumes the armed values ---------------------------------------

    [Test]
    public async Task ArmedAbsoluteTime_BecomesDueExactlyWhenItsEndPasses()
    {
        var item = NewItem(new ItemTemplate { Id = 8, ExpAbsLifetime = 10 });
        ItemLifetimeRules.ApplyNewItemLifespan(item, Now);

        // The tick compares the armed end against the wall clock, so this is the whole
        // precondition for the sweep that removes the item.
        await Assert.That(item.ExpirationTime <= Now.AddMinutes(9)).IsFalse();
        await Assert.That(item.ExpirationTime <= Now.AddMinutes(10)).IsTrue();
    }

    [Test]
    public async Task ArmedOnlineBudget_IsPositiveAndBoundedByItsColumn()
    {
        var item = NewItem(new ItemTemplate { Id = 9, ExpOnlineLifetime = 15 });
        ItemLifetimeRules.ApplyNewItemLifespan(item, Now);

        await Assert.That(item.ExpirationOnlineMinutesLeft > 0.0).IsTrue();
        await Assert.That(item.ExpirationOnlineMinutesLeft <= 15.0).IsTrue();
    }

    // --- lifespan: wire sync -----------------------------------------------------------------------

    [Test]
    public async Task BuildLifespanSyncPackets_CarriesTheArmedEndToTheClient()
    {
        var item = NewItem(new ItemTemplate { Id = 10, ExpAbsLifetime = 120 });
        ItemLifetimeRules.ApplyNewItemLifespan(item, Now);

        var packets = ItemLifetimeRules.BuildLifespanSyncPackets(item, Now);

        await Assert.That(packets.Count).IsEqualTo(1);
        // Read the emitted packet back off the wire rather than trusting the arguments handed to
        // its constructor: the value that reaches the client is the armed expiration instant.
        var stream = new PacketStream();
        packets[0].Write(stream);
        stream.Rollback();
        await Assert.That(stream.ReadBoolean()).IsTrue();
        await Assert.That(stream.ReadUInt64()).IsEqualTo(4242ul);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(10u);
        await Assert.That(stream.ReadDateTime()).IsEqualTo(Now.AddMinutes(120));
    }

    [Test]
    public async Task BuildLifespanSyncPackets_ReportsBothTimersWhenBothArmed()
    {
        var item = NewItem(new ItemTemplate { Id = 11, ExpAbsLifetime = 120, ExpOnlineLifetime = 15 });
        ItemLifetimeRules.ApplyNewItemLifespan(item, Now);

        var packets = ItemLifetimeRules.BuildLifespanSyncPackets(item, Now);

        await Assert.That(packets.Count).IsEqualTo(2);
    }

    [Test]
    public async Task BuildLifespanSyncPackets_IsEmptyForAnItemThatNeverExpires()
    {
        var item = NewItem(new ItemTemplate { Id = 12 });

        var packets = ItemLifetimeRules.BuildLifespanSyncPackets(item, Now);

        await Assert.That(packets.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ReportingTheLifespan_DoesNotRestartTheClock()
    {
        var item = NewItem(new ItemTemplate { Id = 13, ExpAbsLifetime = 120 });
        ItemLifetimeRules.ApplyNewItemLifespan(item, Now);
        var armedEnd = item.ExpirationTime;

        // Re-reading the armed values must not restart the clock, or a long plan would hand out
        // a longer life each time it reported.
        ItemLifetimeRules.BuildLifespanSyncPackets(item, Now.AddMinutes(30));
        ItemLifetimeRules.BuildLifespanSyncPackets(item, Now.AddMinutes(60));

        await Assert.That(item.ExpirationTime).IsEqualTo(armedEnd);
    }

    // --- expedition-level gate ----------------------------------------------------------------------

    [Test]
    public async Task UngatedItem_AllowsAnyGuildLevel()
    {
        var template = new ItemTemplate { Id = 20, ExpeditionLevel = 0 };

        await Assert.That(ItemExpeditionLevelRules.AllowsUse(template, 0)).IsTrue();
        await Assert.That(ItemExpeditionLevelRules.AllowsUse(template, 9)).IsTrue();
    }

    [Test]
    public async Task GatedItem_RefusesAGuildBelowTheFloor()
    {
        var template = new ItemTemplate { Id = 21, ExpeditionLevel = 5 };

        await Assert.That(ItemExpeditionLevelRules.AllowsUse(template, 4)).IsFalse();
    }

    [Test]
    public async Task GatedItem_AllowsAGuildAtOrAboveTheFloor()
    {
        var template = new ItemTemplate { Id = 22, ExpeditionLevel = 5 };

        await Assert.That(ItemExpeditionLevelRules.AllowsUse(template, 5)).IsTrue();
        await Assert.That(ItemExpeditionLevelRules.AllowsUse(template, 6)).IsTrue();
    }

    [Test]
    public async Task GatedItem_RefusesAGuildlessCharacter()
    {
        var template = new ItemTemplate { Id = 23, ExpeditionLevel = 1 };

        // No guild means level 0, which sits below every positive floor.
        await Assert.That(ItemExpeditionLevelRules.AllowsUse(template, 0)).IsFalse();
    }

    [Test]
    public async Task MissingTemplate_CannotBeGated()
    {
        await Assert.That(ItemExpeditionLevelRules.AllowsUse(null, 0)).IsTrue();
    }

    [Test]
    public async Task ItemGate_IsAFloorAndKeepsAdmittingGuildsThatOutgrewIt()
    {
        // The item column names one number, so it is a floor and not a band. The skill-side
        // requirement operator takes two bounds and only admits a level between them; reusing
        // it here would lock a member out as soon as their guild passed the floor.
        var template = new ItemTemplate { Id = 30, ExpeditionLevel = 5 };

        for (uint level = 0; level <= 12; level++)
        {
            var viaItem = ItemExpeditionLevelRules.AllowsUse(template, level);
            var viaBand = UnitReqOperatorRules.PassesExpeditionLevel(5, 5, level);
            await Assert.That(viaItem).IsEqualTo(level >= 5);
            // Pin the difference: past the floor the two must disagree, or the band form is back.
            if (level > 5)
                await Assert.That(viaItem).IsNotEqualTo(viaBand);
        }
    }
}
