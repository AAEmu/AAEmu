using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

public class ItemPersistenceSnapshotTests
{
    [Test]
    public async Task Capture_PreservesEveryPersistedRowFieldAndDetails()
    {
        var container = new ItemContainer(77, SlotType.Inventory, false, null) { ContainerId = 91 };
        var item = new Item(42, new ItemTemplate { Id = 1234 }, 9)
        {
            OwnerId = 77,
            SlotType = SlotType.Inventory,
            Slot = 3,
            Grade = 5,
            ItemFlags = ItemFlag.SoulBound,
            LifespanMins = 17,
            MadeUnitId = 18,
            UnsecureTime = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            UnpackTime = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc),
            CreateTime = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc),
            UccId = 19,
            ExpirationTime = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ExpirationOnlineMinutesLeft = 20.5,
            ChargeStartTime = new DateTime(2026, 4, 5, 6, 7, 8, DateTimeKind.Utc),
            ChargeCount = 21,
            DetailType = (ItemDetailType)9,
            Detail = [1, 2, 3, 4]
        };
        item._holdingContainer = container;
        container.Items.Add(item);

        var snapshot = ItemPersistenceSnapshot.Capture(item);
        var row = snapshot.Expected;

        await Assert.That(row.Id).IsEqualTo(item.Id);
        await Assert.That(row.Type).IsEqualTo(item.GetType().ToString());
        await Assert.That(row.TemplateId).IsEqualTo(item.TemplateId);
        await Assert.That(row.ContainerId).IsEqualTo(container.ContainerId);
        await Assert.That(row.SlotType).IsEqualTo(item.SlotType);
        await Assert.That(row.Slot).IsEqualTo(item.Slot);
        await Assert.That(row.Count).IsEqualTo(item.Count);
        await Assert.That(row.Details.SequenceEqual(item.Detail)).IsTrue();
        await Assert.That(row.LifespanMins).IsEqualTo(item.LifespanMins);
        await Assert.That(row.MadeUnitId).IsEqualTo(item.MadeUnitId);
        await Assert.That(row.UnsecureTime).IsEqualTo(item.UnsecureTime);
        await Assert.That(row.UnpackTime).IsEqualTo(item.UnpackTime);
        await Assert.That(row.OwnerId).IsEqualTo(item.OwnerId);
        await Assert.That(row.CreatedAt).IsEqualTo(item.CreateTime);
        await Assert.That(row.Grade).IsEqualTo(item.Grade);
        await Assert.That(row.Flags).IsEqualTo(item.ItemFlags);
        await Assert.That(row.UccId).IsEqualTo(item.UccId);
        await Assert.That(row.ExpirationTime).IsEqualTo(item.ExpirationTime);
        await Assert.That(row.ExpirationOnlineMinutes).IsEqualTo(item.ExpirationOnlineMinutesLeft);
        await Assert.That(row.ChargeStartTime).IsEqualTo(item.ChargeStartTime);
        await Assert.That(row.ChargeCount).IsEqualTo(item.ChargeCount);

        item.Detail = [9, 9, 9, 9];
        await Assert.That(() => snapshot.ValidateLiveState()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task WithCount_KeepsExpectedGuardAndProjectsOnlyTheRemainingCount()
    {
        var item = new Item(42, new ItemTemplate { Id = 1234 }, 9) { OwnerId = 77, SlotType = SlotType.Inventory, Slot = 3 };

        var snapshot = ItemPersistenceSnapshot.Capture(item).WithCount(4);

        await Assert.That(snapshot.Expected.Count).IsEqualTo(9);
        await Assert.That(snapshot.Desired.Count).IsEqualTo(4);
        snapshot.ValidateLiveState();
        item.Count = 8;
        await Assert.That(() => snapshot.ValidateLiveState()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Capture_UsesTheDirtyCanonicalCountRatherThanAnOlderWorldSave()
    {
        // A previous World snapshot could still hold count=1 here. An API grant has already
        // made the canonical online stack ten, so a one-item debit must write nine.
        var item = new Item(42, new ItemTemplate { Id = 1234 }, 1) { OwnerId = 77, SlotType = SlotType.Inventory, Slot = 3 };
        item.Count = 10;

        var snapshot = ItemPersistenceSnapshot.Capture(item).WithCount(9);

        await Assert.That(snapshot.Expected.Count).IsEqualTo(10);
        await Assert.That(snapshot.Desired.Count).IsEqualTo(9);
        snapshot.ValidateLiveState();
    }

    [Test]
    public async Task WithLocation_ProjectsMailOwnershipWithoutChangingTheLiveItem()
    {
        var source = new ItemContainer(77, SlotType.System, false, null) { ContainerId = 91 };
        var item = new Item(42, new ItemTemplate { Id = 1234 }, 9) { OwnerId = 77, SlotType = SlotType.System, Slot = 3 };
        item._holdingContainer = source;
        source.Items.Add(item);

        var snapshot = ItemPersistenceSnapshot.Capture(item).WithLocation(0, SlotType.Mail, 2, 88);

        await Assert.That(snapshot.Expected.ContainerId).IsEqualTo((ulong)91);
        await Assert.That(snapshot.Desired.ContainerId).IsEqualTo((ulong)0);
        await Assert.That(snapshot.Desired.SlotType).IsEqualTo(SlotType.Mail);
        await Assert.That(snapshot.Desired.Slot).IsEqualTo(2);
        await Assert.That(snapshot.Desired.OwnerId).IsEqualTo((ulong)88);
        await Assert.That(item.OwnerId).IsEqualTo((ulong)77);
        await Assert.That(item._holdingContainer).IsEqualTo(source);
    }

    [Test]
    public async Task ValidateForPersistence_RejectsOccupiedDestinationBeforeDetachingTheSource()
    {
        var source = new ItemContainer(77, SlotType.System, false, null) { ContainerId = 91 };
        var destination = new ItemContainer(77, SlotType.System, false, null) { ContainerId = 92, ContainerSize = 4 };
        var item = new Item(42, new ItemTemplate { Id = 1234 }, 9) { OwnerId = 77, SlotType = SlotType.System, Slot = 1 };
        var occupying = new Item(43, new ItemTemplate { Id = 1235 }, 1) { OwnerId = 77, SlotType = SlotType.System, Slot = 2 };
        item._holdingContainer = source;
        occupying._holdingContainer = destination;
        source.Items.Add(item);
        destination.Items.Add(occupying);

        var snapshot = ItemPersistenceSnapshot.Capture(item).MoveTo(destination, 2);

        await Assert.That(() => snapshot.ValidateForPersistence()).Throws<InvalidOperationException>();
        await Assert.That(source.Items.Contains(item)).IsTrue();
        await Assert.That(destination.Items.Contains(item)).IsFalse();
        await Assert.That(item._holdingContainer).IsEqualTo(source);
    }

    [Test]
    public async Task PersistentContainers_EmitsNewSystemDestinationMetadataWithTheItem()
    {
        var source = new ItemContainer(77, SlotType.Inventory, false, null) { ContainerId = 91 };
        var system = new ItemContainer(77, SlotType.System, false, null) { ContainerId = 92, ContainerSize = 20 };
        var item = new Item(42, new ItemTemplate { Id = 1234 }, 9) { OwnerId = 77, SlotType = SlotType.Inventory, Slot = 1 };
        item._holdingContainer = source;
        source.Items.Add(item);

        var snapshot = ItemPersistenceSnapshot.Capture(item).MoveTo(system, 0);
        var rows = snapshot.PersistentContainers().Select(ItemContainerPersistenceRow.Capture).ToArray();

        await Assert.That(rows.Length).IsEqualTo(2);
        await Assert.That(rows.Single(row => row.ContainerId == 92).SlotType).IsEqualTo(SlotType.System);
        await Assert.That(rows.Single(row => row.ContainerId == 92).OwnerId).IsEqualTo((uint)77);
        await Assert.That(snapshot.Desired.ContainerId).IsEqualTo((ulong)92);
    }
}
