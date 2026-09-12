using System.Runtime.CompilerServices;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Core.Managers;

public sealed class ButlerItemStorageServiceTests
{
    [Test]
    public async Task Publisher_BuildsNativeOwnerScopedAddTasksBeforeAcknowledgement()
    {
        var item = new Item(9001, new ItemTemplate { Id = 15566 }, 1);
        var request = new ButlerItemSwapRequest(2, 4, 2, 0, 0);

        var packets = ButlerItemSwapPublisher.BuildAddedPackets(request, item);
        var player = packets.Player.Write(new PacketStream());
        player.Rollback();
        await Assert.That(player.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(player.ReadByte()).IsEqualTo((byte)ItemTaskType.SwapButlerItem);
        await Assert.That(player.ReadByte()).IsEqualTo((byte)1);
        await Assert.That(player.ReadByte()).IsEqualTo((byte)ItemAction.Seize);
        await Assert.That(player.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(player.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(player.ReadByte()).IsEqualTo((byte)SlotType.Inventory);
        await Assert.That(player.ReadByte()).IsEqualTo((byte)4);
        await Assert.That(player.ReadUInt64()).IsEqualTo(item.Id);
        await Assert.That(player.ReadByte()).IsEqualTo((byte)0); // outer force-remove count

        var butler = packets.Butler.Write(new PacketStream());
        butler.Rollback();
        await Assert.That(butler.ReadByte()).IsEqualTo((byte)7);
        await Assert.That(butler.ReadByte()).IsEqualTo((byte)ItemTaskType.SwapButlerItem);
        await Assert.That(butler.ReadByte()).IsEqualTo((byte)1);
        await Assert.That(butler.ReadByte()).IsEqualTo((byte)0x15);
        await Assert.That(butler.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(butler.ReadByte()).IsEqualTo((byte)7);
        await Assert.That(butler.ReadByte()).IsEqualTo((byte)SlotType.Inventory);
        await Assert.That(butler.ReadByte()).IsEqualTo((byte)4);
        await Assert.That(butler.ReadByte()).IsEqualTo((byte)SlotType.Inventory);
        await Assert.That(butler.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(butler.ReadUInt32()).IsEqualTo(item.TemplateId);

        var acknowledgement = packets.Acknowledgement.Write(new PacketStream());
        acknowledgement.Rollback();
        await Assert.That(acknowledgement.ReadByte()).IsEqualTo((byte)2);
        await Assert.That(acknowledgement.ReadByte()).IsEqualTo((byte)4);
    }

    [Test]
    public async Task AddGarden_CommitsBeforeLiveMoveAndPublishesWhileInventoryIsReserved()
    {
        var (character, bag, system, item) = CreateCharacterWithItem(SlotType.Inventory, 4);
        var butler = new CharacterButler(character.Id) { HouseId = 20 };
        var persistence = new RecordingPersistence
        {
            OnAdd = (_, stored, snapshot) =>
            {
                if (!PersistenceGate.IsOperationHeld ||
                    !Monitor.IsEntered(character.Inventory.MutationSyncRoot) ||
                    !ReferenceEquals(item._holdingContainer, bag) ||
                    butler.StoredItems.Count != 0 ||
                    stored.ItemId != item.Id ||
                    !ReferenceEquals(snapshot.DestinationContainer, system))
                    throw new InvalidOperationException("Storage mutated live state before its durable commit.");
            }
        };
        var publisher = new RecordingPublisher(character.Inventory, butler);
        var service = CreateService(character, butler, persistence, publisher, StorageState(0, 0, 0));

        var result = service.Swap(character, 2, 4, 2, 0, 0);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Acknowledged).IsTrue();
        await Assert.That(item._holdingContainer).IsSameReferenceAs(system);
        await Assert.That(item.SlotType).IsEqualTo(SlotType.System);
        await Assert.That(butler.StoredItems.ContainsKey(item.Id)).IsTrue();
        await Assert.That(publisher.Added).IsEqualTo(1);
        await Assert.That(publisher.PublishedUnderInventoryLease).IsTrue();
    }

    [Test]
    public async Task AddGarden_PersistenceFailurePreservesBagAndAggregate()
    {
        var (character, bag, _, item) = CreateCharacterWithItem(SlotType.Inventory, 4);
        var butler = new CharacterButler(character.Id) { HouseId = 20 };
        var persistence = new RecordingPersistence { ThrowOnAdd = true };
        var publisher = new RecordingPublisher(character.Inventory, butler);
        var service = CreateService(character, butler, persistence, publisher, StorageState(0, 0, 0));

        var result = service.Swap(character, 2, 4, 2, 0, 0);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(ButlerItemStorageFailure.PersistenceFailed);
        await Assert.That(item._holdingContainer).IsSameReferenceAs(bag);
        await Assert.That(item.SlotType).IsEqualTo(SlotType.Inventory);
        await Assert.That(butler.StoredItems).IsEmpty();
        await Assert.That(publisher.Added).IsEqualTo(0);
    }

    [Test]
    public async Task AddGarden_EnforcesCurrentLevelGardenCount()
    {
        var (character, _, system, item) = CreateCharacterWithItem(SlotType.Inventory, 4);
        var butler = new CharacterButler(character.Id) { HouseId = 20 };
        var existing = new Item(700, new ItemTemplate { Id = 15566, MaxCount = 1 }, 1)
        {
            OwnerId = character.Id,
            SlotType = SlotType.System,
            Slot = 1,
            _holdingContainer = system
        };
        system.Items.Add(existing);
        butler.ApplyStoredItem(new ButlerStoredItem(2, existing.Id));
        var persistence = new RecordingPersistence();
        var service = CreateService(character, butler, persistence,
            new RecordingPublisher(character.Inventory, butler), StorageState(1, 180, 0, totalGardenCount: 1));

        var result = service.Swap(character, 2, 4, 2, 0, 0);

        await Assert.That(result.Failure).IsEqualTo(ButlerItemStorageFailure.NoGardenSlot);
        await Assert.That(item.SlotType).IsEqualTo(SlotType.Inventory);
        await Assert.That(persistence.Added).IsEqualTo(0);
    }

    [Test]
    public async Task AddGarden_RejectsBlueprintAboveCurrentHarvestGrade()
    {
        var (character, _, _, item) = CreateCharacterWithItem(SlotType.Inventory, 4);
        var butler = new CharacterButler(character.Id) { HouseId = 20 };
        var persistence = new RecordingPersistence();
        var service = CreateService(character, butler, persistence,
            new RecordingPublisher(character.Inventory, butler), StorageState(0, 0, 0), requiredGrade: 2);

        var result = service.Swap(character, 2, 4, 2, 0, 0);

        await Assert.That(result.Failure).IsEqualTo(ButlerItemStorageFailure.InvalidContent);
        await Assert.That(result.Error).IsEqualTo(ErrorMessageType.ButlerHarvestGradeInsufficient);
        await Assert.That(item.SlotType).IsEqualTo(SlotType.Inventory);
        await Assert.That(persistence.Added).IsEqualTo(0);
    }

    [Test]
    public async Task RemoveGarden_RejectsCapacityStillUsedByActiveJob()
    {
        var (character, _, system, item) = CreateCharacterWithItem(SlotType.System, 19);
        var butler = new CharacterButler(character.Id) { HouseId = 20 };
        butler.ApplyStoredItem(new ButlerStoredItem(2, item.Id));
        var persistence = new RecordingPersistence();
        var service = CreateService(character, butler, persistence,
            new RecordingPublisher(character.Inventory, butler), StorageState(1, 180, 180));

        var result = service.Swap(character, 2, 4, 2, 0, item.Id);

        await Assert.That(result.Failure).IsEqualTo(ButlerItemStorageFailure.GardenAreaInUse);
        await Assert.That(item._holdingContainer).IsSameReferenceAs(system);
        await Assert.That(butler.StoredItems.ContainsKey(item.Id)).IsTrue();
        await Assert.That(persistence.Removed).IsEqualTo(0);
    }

    [Test]
    public async Task RemoveGarden_CommitsBeforeMovingSystemItemToRequestedBagSlot()
    {
        var (character, bag, _, item) = CreateCharacterWithItem(SlotType.System, 19);
        var butler = new CharacterButler(character.Id) { HouseId = 20 };
        butler.ApplyStoredItem(new ButlerStoredItem(2, item.Id));
        var persistence = new RecordingPersistence();
        var publisher = new RecordingPublisher(character.Inventory, butler);
        var service = CreateService(character, butler, persistence, publisher, StorageState(1, 180, 0));

        var result = service.Swap(character, 2, 4, 2, 0, item.Id);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(item._holdingContainer).IsSameReferenceAs(bag);
        await Assert.That(item.SlotType).IsEqualTo(SlotType.Inventory);
        await Assert.That(item.Slot).IsEqualTo(4);
        await Assert.That(butler.StoredItems.ContainsKey(item.Id)).IsFalse();
        await Assert.That(publisher.Removed).IsEqualTo(1);
        await Assert.That(publisher.PublishedUnderInventoryLease).IsTrue();
    }

    [Test]
    public async Task Swap_RejectsNonNativeLocationsBeforeInventoryOrPersistence()
    {
        var (character, _, _, _) = CreateCharacterWithItem(SlotType.Inventory, 4);
        var butler = new CharacterButler(character.Id) { HouseId = 20 };
        var persistence = new RecordingPersistence();
        var publisher = new RecordingPublisher(character.Inventory, butler);
        var service = CreateService(character, butler, persistence, publisher, StorageState(0, 0, 0));

        var result = service.Swap(character, 3, 4, 2, 0, 0);

        await Assert.That(result.Failure).IsEqualTo(ButlerItemStorageFailure.InvalidLocation);
        await Assert.That(result.Acknowledged).IsFalse();
        await Assert.That(persistence.Added).IsEqualTo(0);
        await Assert.That(publisher.Added).IsEqualTo(0);
    }

    private static ButlerItemStorageService CreateService(
        Character character,
        CharacterButler butler,
        RecordingPersistence persistence,
        RecordingPublisher publisher,
        ButlerGardenStorageState state,
        uint requiredGrade = 1)
    {
        var manager = Mock.Of<IButlerManager>();
        manager.GetOrCreate(character.Id).Returns(butler);
        var house = CreateHouse(butler.HouseId, character.Id);
        var housing = Mock.Of<IHousingManager>();
        housing.GetHouseById(butler.HouseId).Returns(house);
        var items = Mock.Of<IItemManager>();
        items.CapturePersistenceSnapshot(Any<Item>())
            .Returns((Item item) => ItemPersistenceSnapshot.Capture(item));
        items.GetItemByItemId(Any<ulong>()).Returns((ulong itemId) =>
            character.Inventory._itemContainers.Values
                .SelectMany(container => container.Items)
                .FirstOrDefault(item => item.Id == itemId));
        items.ApplyCommittedSnapshot(Any<ItemPersistenceSnapshot>())
            .Callback(ApplyCommittedSnapshot);
        var resolver = new RecordingResolver(state, requiredGrade);
        return new ButlerItemStorageService(
            manager.Object, housing.Object, items.Object, persistence, resolver, publisher);
    }

    private static ButlerGardenStorageState StorageState(
        uint count,
        uint landSize,
        uint activeLandSize,
        uint totalGardenCount = 2) => new(
        new ButlerLevel { ButlerId = 1, TotalGardenCount = totalGardenCount },
        1,
        count,
        landSize,
        0,
        activeLandSize,
        0);

    private static House CreateHouse(uint id, uint ownerId)
    {
        var house = new House { Id = id, OwnerId = ownerId };
        typeof(House).GetField("_currentStep",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(house, -1);
        return house;
    }

    private static (Character Character, ItemContainer Bag, ItemContainer System, Item Item)
        CreateCharacterWithItem(SlotType location, int slot)
    {
        const uint characterId = 10;
        var character = new Character(new UnitCustomModelParams()) { Id = characterId };
        var inventory = (Inventory)RuntimeHelpers.GetUninitializedObject(typeof(Inventory));
        typeof(Inventory).GetField(nameof(Inventory.Owner))!.SetValue(inventory, character);
        var bag = new ItemContainer(characterId, SlotType.Inventory, false, character)
        {
            Owner = character,
            ContainerId = 501,
            ContainerSize = 50
        };
        var system = new ItemContainer(characterId, SlotType.System, false, character)
        {
            Owner = character,
            ContainerId = 502,
            ContainerSize = -1
        };
        var source = location == SlotType.Inventory ? bag : system;
        var item = new Item(9001, new ItemTemplate { Id = 15566, MaxCount = 1 }, 1)
        {
            OwnerId = characterId,
            SlotType = location,
            Slot = slot,
            _holdingContainer = source
        };
        source.Items.Add(item);
        source.UpdateFreeSlotCount();
        typeof(Inventory).GetProperty(nameof(Inventory.Bag))!.SetValue(inventory, bag);
        typeof(Inventory).GetProperty(nameof(Inventory.SystemContainer))!.SetValue(inventory, system);
        typeof(Inventory).GetProperty(nameof(Inventory._itemContainers))!.SetValue(inventory,
            new Dictionary<SlotType, ItemContainer>
            {
                [SlotType.Inventory] = bag,
                [SlotType.System] = system
            });
        character.Inventory = inventory;
        return (character, bag, system, item);
    }

    private static void ApplyCommittedSnapshot(ItemPersistenceSnapshot snapshot)
    {
        var item = snapshot.Item;
        snapshot.ExpectedContainer.Items.Remove(item);
        snapshot.ExpectedContainer.UpdateFreeSlotCount();
        snapshot.DestinationContainer.Items.Add(item);
        snapshot.DestinationContainer.UpdateFreeSlotCount();
        item._holdingContainer = snapshot.DestinationContainer;
        item.OwnerId = snapshot.Desired.OwnerId;
        item.SlotType = snapshot.Desired.SlotType;
        item.Slot = snapshot.Desired.Slot;
        item.Count = snapshot.Desired.Count;
    }

    private sealed class RecordingResolver(ButlerGardenStorageState state, uint requiredGrade)
        : IButlerGardenStorageResolver
    {
        public bool TryResolveGarden(uint itemTemplateId, out ButlerGardenStorageItem garden)
        {
            garden = new ButlerGardenStorageItem(itemTemplateId, 180, false, requiredGrade);
            return itemTemplateId == 15566;
        }

        public bool TryResolveGardenStorage(CharacterButler butler, out ButlerGardenStorageState resolved)
        {
            resolved = state;
            return true;
        }
    }

    private sealed class RecordingPersistence : IButlerItemStoragePersistence
    {
        public int Added { get; private set; }
        public int Removed { get; private set; }
        public bool ThrowOnAdd { get; init; }
        public Action<uint, ButlerStoredItem, ItemPersistenceSnapshot> OnAdd { get; init; }

        public void Add(uint characterId, ButlerStoredItem storedItem, ItemPersistenceSnapshot snapshot)
        {
            Added++;
            OnAdd?.Invoke(characterId, storedItem, snapshot);
            if (ThrowOnAdd)
                throw new InvalidOperationException("Simulated persistence failure");
        }

        public bool Remove(uint characterId, ulong itemId, ItemPersistenceSnapshot snapshot)
        {
            Removed++;
            return true;
        }
    }

    private sealed class RecordingPublisher(Inventory inventory, CharacterButler butler)
        : IButlerItemSwapPublisher
    {
        public int Added { get; private set; }
        public int Removed { get; private set; }
        public bool PublishedUnderInventoryLease { get; private set; }

        public void PublishAdded(Character character, ButlerItemSwapRequest request, Item item)
        {
            Added++;
            PublishedUnderInventoryLease = Monitor.IsEntered(inventory.MutationSyncRoot) &&
                                           butler.StoredItems.ContainsKey(item.Id);
        }

        public void PublishRemoved(Character character, ButlerItemSwapRequest request, Item item)
        {
            Removed++;
            PublishedUnderInventoryLease = Monitor.IsEntered(inventory.MutationSyncRoot) &&
                                           !butler.StoredItems.ContainsKey(item.Id);
        }
    }
}
