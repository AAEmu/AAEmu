using System.Reflection;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// The item factory is the only place every delivery path goes through, so a lifespan column
/// that is not honoured there is not honoured by loot, mail, cash shop, crafting, housing,
/// indun rewards or an auction payout - the item simply never expires.
/// </summary>
public class ItemFactoryLifespanTests
{
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
    public async Task Factory_ArmsTheAbsoluteLifetimeOnTheStackItReturns()
    {
        var template = new ItemTemplate { Id = 700, MaxCount = 10, ExpAbsLifetime = 1440 };
        var manager = CreateManager(template);

        var item = manager.Create(700, 1, 0, generateId: false);

        await Assert.That(item).IsNotNull();
        await Assert.That(item.ExpirationTime).IsEqualTo(item.CreateTime.AddMinutes(1440));
    }

    [Test]
    public async Task Factory_ArmsTheOnlineLifetimeOnTheStackItReturns()
    {
        var template = new ItemTemplate { Id = 701, MaxCount = 10, ExpOnlineLifetime = 45 };
        var manager = CreateManager(template);

        var item = manager.Create(701, 1, 0, generateId: false);

        await Assert.That(item.ExpirationOnlineMinutesLeft).IsEqualTo(45.0);
    }

    [Test]
    public async Task Factory_HonoursTheSoonerOfTheAbsoluteEndAndTheRelativeColumn()
    {
        var absolute = new DateTime(2027, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var template = new ItemTemplate { Id = 702, MaxCount = 10, ExpAbsLifetime = 60, ExpDate = absolute };
        var manager = CreateManager(template);

        var item = manager.Create(702, 1, 0, generateId: false);

        // A row that names both ends has to end at the sooner of them. Letting the absolute date
        // always win would have made the 44 shipped rows that set both produce an item that is born
        // already expired, and would have thrown away the 60-minute term the row also states.
        await Assert.That(item.ExpirationTime).IsEqualTo(item.CreateTime.AddMinutes(60));
    }

    [Test]
    public async Task Factory_HonoursAnAbsoluteEndDateThatIsTheOnlyEndTheRowNames()
    {
        var absolute = new DateTime(2027, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var template = new ItemTemplate { Id = 712, MaxCount = 10, ExpDate = absolute };
        var manager = CreateManager(template);

        var item = manager.Create(712, 1, 0, generateId: false);

        await Assert.That(item.ExpirationTime).IsEqualTo(absolute);
    }

    [Test]
    public async Task Factory_LeavesAnUntimedItemWithoutATimer()
    {
        var template = new ItemTemplate { Id = 703, MaxCount = 10 };
        var manager = CreateManager(template);

        var item = manager.Create(703, 1, 0, generateId: false);

        await Assert.That(item.ExpirationTime).IsEqualTo(DateTime.MinValue);
        await Assert.That(item.ExpirationOnlineMinutesLeft).IsEqualTo(0.0);
    }

    [Test]
    public async Task Factory_LoadsTheGuildLevelFloorOntoTheTemplate()
    {
        // The gate reads its number off the template, so a row that never loaded the column
        // would silently ungate every item in the catalogue.
        var template = new ItemTemplate { Id = 704, MaxCount = 10, ExpeditionLevel = 6 };
        var manager = CreateManager(template);

        var item = manager.Create(704, 1, 0, generateId: false);

        await Assert.That(item.Template.ExpeditionLevel).IsEqualTo(6u);
        await Assert.That(ItemExpeditionLevelRules.AllowsUse(item.Template, 5)).IsFalse();
        await Assert.That(ItemExpeditionLevelRules.AllowsUse(item.Template, 6)).IsTrue();
    }

    [Test]
    public async Task SyncReportsTheArmedEndEvenWhenNoRelativeColumnProducedIt()
    {
        // An item whose only end is an absolute date has an armed expiration but no
        // exp_abs_lifetime to read it back from. The report has to follow the armed value, or
        // the client is told the item never expires.
        var absolute = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var template = new ItemTemplate { Id = 705, MaxCount = 10, ExpDate = absolute };
        var manager = CreateManager(template);

        var item = manager.Create(705, 1, 0, generateId: false);
        var packets = ItemLifetimeRules.BuildLifespanSyncPackets(item, DateTime.UtcNow);

        await Assert.That(packets.Count).IsEqualTo(1);
        var stream = new PacketStream();
        packets[0].Write(stream);
        stream.Rollback();
        stream.ReadBoolean();
        stream.ReadUInt64();
        stream.ReadUInt32();
        await Assert.That(stream.ReadDateTime()).IsEqualTo(absolute);
    }

    [Test]
    public async Task SyncReportsTheCreationInstantNotTheReportingInstant()
    {
        // The armed end is anchored to when the item was made. Reporting it much later must
        // not slide the end forward to the reporting moment.
        var template = new ItemTemplate { Id = 706, MaxCount = 10, ExpAbsLifetime = 60 };
        var manager = CreateManager(template);

        var item = manager.Create(706, 1, 0, generateId: false);
        var armedEnd = item.ExpirationTime;
        var reportedAt = DateTime.UtcNow.AddHours(5);
        var packets = ItemLifetimeRules.BuildLifespanSyncPackets(item, reportedAt);

        await Assert.That(packets.Count).IsEqualTo(1);
        var stream = new PacketStream();
        packets[0].Write(stream);
        stream.Rollback();
        stream.ReadBoolean();
        stream.ReadUInt64();
        stream.ReadUInt32();
        var onTheWire = stream.ReadDateTime();

        // The stream carries whole seconds, so compare at that resolution. The claim under test
        // is the anchor: the reported end is the one the item was created with, not one slid
        // forward to the reporting moment five hours later.
        await Assert.That(Math.Abs((onTheWire - armedEnd).TotalSeconds)).IsLessThan(1.0);
        await Assert.That(Math.Abs((onTheWire - reportedAt).TotalHours)).IsGreaterThan(4.0);
    }
}
