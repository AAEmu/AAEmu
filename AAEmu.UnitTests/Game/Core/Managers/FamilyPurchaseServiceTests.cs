using System.Runtime.CompilerServices;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Families;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class FamilyPurchaseServiceTests
{
    [Test]
    public async Task Expand_SuccessAppliesTheSameCommittedSnapshotsAndFamilyCount()
    {
        var (character, item) = CreateCharacterWithItem(48995, 5);
        var family = new Family { Id = 10, IncreasedMemberCount = 1 };
        character.Family = family.Id;
        var items = CreateItemManager();
        var repository = Mock.Of<IFamilyPurchaseRepository>();
        repository.TryCommitExpansion(family.Id, 1, 2, Any<IReadOnlyList<ItemPersistenceSnapshot>>())
            .Returns(true);
        var service = new FamilyPurchaseService(repository.Object, items.Object);

        var result = service.Expand(character, family, 48995, 4);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(item.Count).IsEqualTo(1);
        await Assert.That(family.IncreasedMemberCount).IsEqualTo(2u);
        repository.TryCommitExpansion(family.Id, 1, 2, Any<IReadOnlyList<ItemPersistenceSnapshot>>())
            .WasCalled(Times.Once);
    }

    [Test]
    public async Task Invitation_MissingCertificateDoesNotReachPersistence()
    {
        ContentConfigGameData.Instance.SetForTest(FamilyContentConfig.JoinLeaveItemKey, 41419);
        var (character, item) = CreateCharacterWithItem(48995, 1);
        var items = CreateItemManager();
        var repository = Mock.Of<IFamilyPurchaseRepository>();
        var service = new FamilyPurchaseService(repository.Object, items.Object);

        var result = service.ConsumeInvitation(character);

        await Assert.That(result.Failure).IsEqualTo(FamilyPurchaseFailure.MissingItems);
        await Assert.That(item.Count).IsEqualTo(1);
        repository.CommitItemConsumption(Any<IReadOnlyList<ItemPersistenceSnapshot>>()).WasCalled(Times.Never);
    }

    [Test]
    public async Task Expand_ConcurrentFamilyRowChange_DoesNotConsumeOrMutateLiveState()
    {
        var (character, item) = CreateCharacterWithItem(48995, 4);
        var family = new Family { Id = 10, IncreasedMemberCount = 2 };
        character.Family = family.Id;
        var items = CreateItemManager();
        var repository = Mock.Of<IFamilyPurchaseRepository>();
        repository.TryCommitExpansion(family.Id, 2, 3, Any<IReadOnlyList<ItemPersistenceSnapshot>>())
            .Returns(false);
        var service = new FamilyPurchaseService(repository.Object, items.Object);

        var result = service.Expand(character, family, 48995, 4);

        await Assert.That(result.Failure).IsEqualTo(FamilyPurchaseFailure.ConcurrentChange);
        await Assert.That(item.Count).IsEqualTo(4);
        await Assert.That(family.IncreasedMemberCount).IsEqualTo(2u);
        items.ApplyCommittedSnapshot(Any<ItemPersistenceSnapshot>()).WasCalled(Times.Never);
    }

    [Test]
    public async Task Rename_PersistenceFailure_DoesNotConsumeOrMutateLiveState()
    {
        ContentConfigGameData.Instance.SetForTest(FamilyContentConfig.NameChangeItemKey, 48996);
        ContentConfigGameData.Instance.SetForTest(FamilyContentConfig.NameChangeItemCountKey, 1);
        var (character, item) = CreateCharacterWithItem(48996, 1);
        var family = new Family { Id = 10, Name = "Old Name", ChangeNameTime = 100 };
        character.Family = family.Id;
        var items = CreateItemManager();
        var repository = Mock.Of<IFamilyPurchaseRepository>();
        repository.TryCommitRename(family.Id, family.Name, family.ChangeNameTime, "New Name", 200,
                Any<IReadOnlyList<ItemPersistenceSnapshot>>())
            .Throws(new InvalidOperationException("database unavailable"));
        var service = new FamilyPurchaseService(repository.Object, items.Object);

        var result = service.Rename(character, family, "New Name", 200);

        await Assert.That(result.Failure).IsEqualTo(FamilyPurchaseFailure.PersistenceFailed);
        await Assert.That(item.Count).IsEqualTo(1);
        await Assert.That(family.Name).IsEqualTo("Old Name");
        await Assert.That(family.ChangeNameTime).IsEqualTo(100L);
        items.ApplyCommittedSnapshot(Any<ItemPersistenceSnapshot>()).WasCalled(Times.Never);
    }

    [Test]
    public async Task Departure_ConsumesTheCertificateWithTheFamilySave()
    {
        ContentConfigGameData.Instance.SetForTest(FamilyContentConfig.JoinLeaveItemKey, 41419);
        var (character, item) = CreateCharacterWithItem(41419, 2);
        var family = new Family { Id = 10 };
        character.Family = family.Id;
        var items = CreateItemManager();
        var repository = Mock.Of<IFamilyPurchaseRepository>();
        var service = new FamilyPurchaseService(repository.Object, items.Object);

        var result = service.ConsumeDeparture(character, family);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(item.Count).IsEqualTo(1);
        repository.CommitDeparture(family, Any<IReadOnlyList<ItemPersistenceSnapshot>>()).WasCalled(Times.Once);
    }

    [Test]
    public async Task Departure_MissingCertificateDoesNotReachPersistence()
    {
        ContentConfigGameData.Instance.SetForTest(FamilyContentConfig.JoinLeaveItemKey, 41419);
        var (character, item) = CreateCharacterWithItem(48995, 1);
        var family = new Family { Id = 10 };
        var items = CreateItemManager();
        var repository = Mock.Of<IFamilyPurchaseRepository>();
        var service = new FamilyPurchaseService(repository.Object, items.Object);

        var result = service.ConsumeDeparture(character, family);

        await Assert.That(result.Failure).IsEqualTo(FamilyPurchaseFailure.MissingItems);
        await Assert.That(item.Count).IsEqualTo(1);
        repository.CommitDeparture(Any<Family>(), Any<IReadOnlyList<ItemPersistenceSnapshot>>())
            .WasCalled(Times.Never);
    }

    [Test]
    public async Task Departure_PersistenceFailure_DoesNotConsumeTheCertificate()
    {
        ContentConfigGameData.Instance.SetForTest(FamilyContentConfig.JoinLeaveItemKey, 41419);
        var (character, item) = CreateCharacterWithItem(41419, 1);
        var family = new Family { Id = 10 };
        var items = CreateItemManager();
        var repository = Mock.Of<IFamilyPurchaseRepository>();
        repository.CommitDeparture(family, Any<IReadOnlyList<ItemPersistenceSnapshot>>())
            .Throws(new InvalidOperationException("database unavailable"));
        var service = new FamilyPurchaseService(repository.Object, items.Object);

        var result = service.ConsumeDeparture(character, family);

        await Assert.That(result.Failure).IsEqualTo(FamilyPurchaseFailure.PersistenceFailed);
        await Assert.That(item.Count).IsEqualTo(1);
        items.ApplyCommittedSnapshot(Any<ItemPersistenceSnapshot>()).WasCalled(Times.Never);
    }

    private static Mock<IItemManager> CreateItemManager()
    {
        var manager = Mock.Of<IItemManager>();
        manager.CapturePersistenceSnapshot(Any<Item>())
            .Returns((Item item) => ItemPersistenceSnapshot.Capture(item));
        manager.ApplyCommittedSnapshot(Any<ItemPersistenceSnapshot>())
            .Callback((ItemPersistenceSnapshot snapshot) => snapshot.Item.Count = snapshot.Desired.Count);
        return manager;
    }

    private static (Character Character, Item Item) CreateCharacterWithItem(uint templateId, int count)
    {
        const uint characterId = 71;
        var character = new Character(new UnitCustomModelParams()) { Id = characterId };
        var inventory = (Inventory)RuntimeHelpers.GetUninitializedObject(typeof(Inventory));
        typeof(Inventory).GetField(nameof(Inventory.Owner))!.SetValue(inventory, character);
        var bag = new ItemContainer(characterId, SlotType.Inventory, false, character)
        {
            Owner = character,
            ContainerId = 501,
            ContainerSize = 50
        };
        var item = new Item(9001, new ItemTemplate { Id = templateId, MaxCount = 1000 }, count)
        {
            OwnerId = characterId,
            SlotType = SlotType.Inventory,
            Slot = 4,
            _holdingContainer = bag
        };
        bag.Items.Add(item);
        bag.UpdateFreeSlotCount();
        typeof(Inventory).GetProperty(nameof(Inventory.Bag))!.SetValue(inventory, bag);
        typeof(Inventory).GetProperty(nameof(Inventory._itemContainers))!.SetValue(inventory,
            new Dictionary<SlotType, ItemContainer> { [SlotType.Inventory] = bag });
        character.Inventory = inventory;
        return (character, item);
    }
}
