using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Containers;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Core.Managers;

public enum ButlerItemStorageFailure
{
    None,
    InvalidLocation,
    NotBound,
    InvalidContent,
    NoGardenSlot,
    BagFull,
    Busy,
    GardenAreaInUse,
    ConcurrentChange,
    PersistenceFailed
}

public readonly record struct ButlerGardenStorageItem(
    uint ItemTemplateId,
    uint GardenSize,
    bool IsUnderWater,
    uint RequiredHarvestGrade);

public readonly record struct ButlerGardenStorageState(
    ButlerLevel CurrentLevel,
    uint CurrentHarvestGrade,
    uint HeldGardenCount,
    uint LandGardenSize,
    uint WaterGardenSize,
    uint ActiveLandGardenSize,
    uint ActiveWaterGardenSize);

/// <summary>
/// Resolves garden blueprints, the current farmhand level, and occupied land/water area from
/// immutable game content and the already-locked aggregate. Implementations must not acquire
/// ButlerManager, House, inventory, or database locks.
/// </summary>
public interface IButlerGardenStorageResolver
{
    bool TryResolveGarden(uint itemTemplateId, out ButlerGardenStorageItem garden);
    bool TryResolveGardenStorage(CharacterButler butler, out ButlerGardenStorageState state);
}

public readonly record struct ButlerItemSwapRequest(
    byte BagType,
    byte BagIndex,
    byte ButlerType,
    byte ButlerIndex,
    ulong ButlerItemId);

public readonly record struct ButlerItemStorageResult(
    bool Success,
    bool Acknowledged,
    ButlerItemStorageFailure Failure,
    ErrorMessageType Error);

public interface IButlerItemSwapPublisher
{
    void PublishAdded(Character character, ButlerItemSwapRequest request, Item item);
    void PublishRemoved(Character character, ButlerItemSwapRequest request, Item item);
}

public interface IButlerItemStoragePersistence
{
    void Add(uint characterId, ButlerStoredItem storedItem, ItemPersistenceSnapshot snapshot);
    bool Remove(uint characterId, ulong itemId, ItemPersistenceSnapshot snapshot);
}

/// <summary>Commits the item-container row and farmhand ownership row in one MySQL transaction.</summary>
public sealed class ButlerItemStoragePersistence : IButlerItemStoragePersistence
{
    private readonly IButlerRepository _repository;
    private readonly IItemManager _itemManager;
    private readonly Func<MySqlConnection> _openConnection;

    public ButlerItemStoragePersistence(IButlerRepository repository, IItemManager itemManager)
        : this(repository, itemManager, MySQL.CreateConnection)
    {
    }

    internal ButlerItemStoragePersistence(
        IButlerRepository repository,
        IItemManager itemManager,
        Func<MySqlConnection> openConnection)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _itemManager = itemManager ?? throw new ArgumentNullException(nameof(itemManager));
        _openConnection = openConnection ?? throw new ArgumentNullException(nameof(openConnection));
    }

    public void Add(uint characterId, ButlerStoredItem storedItem, ItemPersistenceSnapshot snapshot)
    {
        using var connection = _openConnection();
        using var transaction = connection.BeginTransaction();
        _itemManager.PersistSnapshots(connection, transaction, [snapshot]);
        _repository.SaveStoredItem(characterId, storedItem, connection, transaction);
        transaction.Commit();
    }

    public bool Remove(uint characterId, ulong itemId, ItemPersistenceSnapshot snapshot)
    {
        using var connection = _openConnection();
        using var transaction = connection.BeginTransaction();
        _itemManager.PersistSnapshots(connection, transaction, [snapshot]);
        if (!_repository.DeleteStoredItem(characterId, itemId, connection, transaction))
        {
            transaction.Rollback();
            return false;
        }
        transaction.Commit();
        return true;
    }
}

/// <summary>Publishes the two owner-scoped item tasks before acknowledging the optimistic swap.</summary>
public sealed class ButlerItemSwapPublisher : IButlerItemSwapPublisher
{
    private const byte PlayerOwnerType = 0;
    private const byte ButlerOwnerType = 7;
    private static readonly List<ulong> NoForceRemove = [];

    public void PublishAdded(Character character, ButlerItemSwapRequest request, Item item)
    {
        var packets = BuildAddedPackets(request, item);
        character.SendPacket(packets.Player);
        character.SendPacket(packets.Butler);
        character.SendPacket(packets.Acknowledgement);
    }

    internal static (SCItemTaskSuccessPacket Player, SCItemTaskSuccessPacket Butler,
        SCButlerItemSwappedPacket Acknowledgement) BuildAddedPackets(ButlerItemSwapRequest request, Item item) =>
        (new SCItemTaskSuccessPacket(
            ItemTaskType.SwapButlerItem,
            new ItemRemoveSlot(item.Id, SlotType.Inventory, request.BagIndex, PlayerOwnerType),
            NoForceRemove,
            PlayerOwnerType),
        new SCItemTaskSuccessPacket(
            ItemTaskType.SwapButlerItem,
            new ItemAddAtLocation(item, SlotType.Inventory, 0),
            NoForceRemove,
            ButlerOwnerType),
        CreateAcknowledgement(request));

    public void PublishRemoved(Character character, ButlerItemSwapRequest request, Item item)
    {
        var packets = BuildRemovedPackets(request, item);
        character.SendPacket(packets.Butler);
        character.SendPacket(packets.Player);
        character.SendPacket(packets.Acknowledgement);
    }

    internal static (SCItemTaskSuccessPacket Butler, SCItemTaskSuccessPacket Player,
        SCButlerItemSwappedPacket Acknowledgement) BuildRemovedPackets(ButlerItemSwapRequest request, Item item) =>
        (new SCItemTaskSuccessPacket(
            ItemTaskType.SwapButlerItem,
            new ItemRemoveSlot(item.Id, SlotType.Inventory, 0, ButlerOwnerType),
            NoForceRemove,
            ButlerOwnerType),
        new SCItemTaskSuccessPacket(
            ItemTaskType.SwapButlerItem,
            new ItemAddAtLocation(item, SlotType.Inventory, request.BagIndex),
            NoForceRemove,
            PlayerOwnerType),
        CreateAcknowledgement(request));

    private static SCButlerItemSwappedPacket CreateAcknowledgement(ButlerItemSwapRequest request) =>
        new(
            request.BagType,
            request.BagIndex,
            request.ButlerType,
            request.ButlerIndex,
            request.ButlerItemId,
            (ushort)ErrorMessageType.NoErrorMessage);
}

/// <summary>
/// Moves garden blueprints between the player's bag and durable System container. The item row and
/// logical farmhand row commit in one narrow transaction; live state and client packets follow only
/// after that commit while the inventory mutation lease still preserves FIFO ordering.
/// </summary>
public sealed class ButlerItemStorageService
{
    private const byte GardenLocationType = (byte)SlotType.Inventory;
    private const byte ButlerGardenLocationIndex = 0;

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly IButlerManager _butlerManager;
    private readonly IHousingManager _housingManager;
    private readonly IItemManager _itemManager;
    private readonly IButlerItemStoragePersistence _persistence;
    private readonly IButlerGardenStorageResolver _resolver;
    private readonly IButlerItemSwapPublisher _publisher;

    public ButlerItemStorageService(
        IButlerManager butlerManager,
        IHousingManager housingManager,
        IItemManager itemManager,
        IButlerItemStoragePersistence persistence,
        IButlerGardenStorageResolver resolver,
        IButlerItemSwapPublisher publisher)
    {
        _butlerManager = butlerManager ?? throw new ArgumentNullException(nameof(butlerManager));
        _housingManager = housingManager ?? throw new ArgumentNullException(nameof(housingManager));
        _itemManager = itemManager ?? throw new ArgumentNullException(nameof(itemManager));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
    }

    public ButlerItemStorageResult Swap(
        Character character,
        byte bagType,
        byte bagIndex,
        byte butlerType,
        byte butlerIndex,
        ulong butlerItemId)
    {
        var request = new ButlerItemSwapRequest(bagType, bagIndex, butlerType, butlerIndex, butlerItemId);
        if (character == null || bagType != GardenLocationType || butlerType != GardenLocationType ||
            butlerIndex != ButlerGardenLocationIndex)
            return Failed(ButlerItemStorageFailure.InvalidLocation, ErrorMessageType.ItemUpdateFail);

        var butler = _butlerManager.GetOrCreate(character.Id);
        PersistenceGate.EnterOperation();
        try
        {
            uint expectedHouseId;
            lock (butler.SyncRoot)
                expectedHouseId = butler.HouseId;
            if (expectedHouseId == 0)
                return Failed(ButlerItemStorageFailure.NotBound, ErrorMessageType.InteractionPermissionDeny);

            var house = _housingManager.GetHouseById(expectedHouseId);
            if (house == null)
                return Failed(ButlerItemStorageFailure.NotBound, ErrorMessageType.InteractionPermissionDeny);

            lock (house.LifecycleSyncRoot)
            lock (butler.OperationSyncRoot)
            lock (butler.SyncRoot)
            {
                if (butler.IsDeleted || butler.HouseId != expectedHouseId || house.IsRemovedFromWorld ||
                    !ReferenceEquals(_housingManager.GetHouseById(expectedHouseId), house) ||
                    house.OwnerId != character.Id || house.CurrentStep != -1)
                    return Failed(ButlerItemStorageFailure.NotBound, ErrorMessageType.InteractionPermissionDeny);

                if (!character.Inventory.TryAcquireFarmhandMutation(out var inventoryLease))
                    return Failed(ButlerItemStorageFailure.Busy, ErrorMessageType.ItemLocked);

                using (inventoryLease)
                    return butlerItemId == 0
                        ? AddGarden(character, butler, request)
                        : RemoveGarden(character, butler, request);
            }
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }
    }

    private ButlerItemStorageResult AddGarden(
        Character character,
        CharacterButler butler,
        ButlerItemSwapRequest request)
    {
        var bag = character.Inventory.Bag;
        var system = character.Inventory.SystemContainer;
        var item = bag?.GetItemBySlot(request.BagIndex);
        if (item == null || item.Id == 0 || system == null || item.Count != 1 || item.OwnerId != character.Id ||
            !ReferenceEquals(item._holdingContainer, bag) || item.SlotType != SlotType.Inventory ||
            item.Slot != request.BagIndex || butler.StoredItems.ContainsKey(item.Id) ||
            !HasCoherentStoredItems(character, butler, system))
            return Failed(ButlerItemStorageFailure.InvalidContent, ErrorMessageType.ItemUpdateFail);

        if (!_resolver.TryResolveGarden(item.TemplateId, out var garden) ||
            garden.ItemTemplateId != item.TemplateId ||
            !_resolver.TryResolveGardenStorage(butler, out var state) ||
            !ValidStorageState(butler, state) || garden.RequiredHarvestGrade == 0)
            return Failed(ButlerItemStorageFailure.InvalidContent, ErrorMessageType.ItemUpdateFail);
        if (garden.RequiredHarvestGrade > state.CurrentHarvestGrade)
            return Failed(ButlerItemStorageFailure.InvalidContent,
                ErrorMessageType.ButlerHarvestGradeInsufficient);

        if (state.HeldGardenCount >= state.CurrentLevel.TotalGardenCount)
            return Failed(ButlerItemStorageFailure.NoGardenSlot, ErrorMessageType.ItemPickupLimit);

        var systemSlot = system.GetUnusedSlot(-1);
        if (systemSlot < 0)
            return Failed(ButlerItemStorageFailure.Busy, ErrorMessageType.ItemUpdateFail);

        var snapshot = _itemManager.CapturePersistenceSnapshot(item).MoveTo(system, systemSlot);
        var stored = new ButlerStoredItem(GardenLocationType, item.Id);
        try
        {
            _persistence.Add(butler.CharacterId, stored, snapshot);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to store farmhand garden item {0} for character {1}",
                item.Id, butler.CharacterId);
            return Failed(ButlerItemStorageFailure.PersistenceFailed, ErrorMessageType.InternalError);
        }

        _itemManager.ApplyCommittedSnapshot(snapshot);
        butler.ApplyStoredItem(stored);
        _publisher.PublishAdded(character, request, item);
        return Succeeded();
    }

    private ButlerItemStorageResult RemoveGarden(
        Character character,
        CharacterButler butler,
        ButlerItemSwapRequest request)
    {
        var bag = character.Inventory.Bag;
        var system = character.Inventory.SystemContainer;
        if (bag == null || system == null || !butler.StoredItems.TryGetValue(request.ButlerItemId, out var stored) ||
            stored.Type != GardenLocationType || !HasCoherentStoredItems(character, butler, system))
            return Failed(ButlerItemStorageFailure.InvalidContent, ErrorMessageType.ItemUpdateFail);

        var item = system.GetItemByItemId(request.ButlerItemId);
        if (item == null || item.Count != 1 || item.OwnerId != character.Id ||
            !ReferenceEquals(item._holdingContainer, system) || item.SlotType != SlotType.System)
            return Failed(ButlerItemStorageFailure.InvalidContent, ErrorMessageType.ItemUpdateFail);

        if (bag.ContainerSize >= 0 && request.BagIndex >= bag.ContainerSize)
            return Failed(ButlerItemStorageFailure.InvalidLocation, ErrorMessageType.ItemUpdateFail);
        if (bag.GetUnusedSlot(request.BagIndex) != request.BagIndex)
            return Failed(ButlerItemStorageFailure.BagFull, ErrorMessageType.BagFull);

        if (!_resolver.TryResolveGarden(item.TemplateId, out var garden) ||
            garden.ItemTemplateId != item.TemplateId ||
            !_resolver.TryResolveGardenStorage(butler, out var state) ||
            !ValidStorageState(butler, state))
            return Failed(ButlerItemStorageFailure.InvalidContent, ErrorMessageType.ItemUpdateFail);
        if (!CanRemoveGarden(garden, state))
            return Failed(ButlerItemStorageFailure.GardenAreaInUse, ErrorMessageType.ItemUpdateFail);

        var snapshot = _itemManager.CapturePersistenceSnapshot(item).MoveTo(bag, request.BagIndex);
        try
        {
            if (!_persistence.Remove(butler.CharacterId, item.Id, snapshot))
                return Failed(ButlerItemStorageFailure.ConcurrentChange, ErrorMessageType.ItemUpdateFail);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to return farmhand garden item {0} for character {1}",
                item.Id, butler.CharacterId);
            return Failed(ButlerItemStorageFailure.PersistenceFailed, ErrorMessageType.InternalError);
        }

        _itemManager.ApplyCommittedSnapshot(snapshot);
        if (!butler.RemoveStoredItem(item.Id))
            throw new InvalidOperationException($"Committed farmhand item {item.Id} vanished from live state.");
        _publisher.PublishRemoved(character, request, item);
        return Succeeded();
    }

    private static bool ValidStorageState(CharacterButler butler, ButlerGardenStorageState state) =>
        state.CurrentLevel is { ButlerId: > 0, TotalGardenCount: > 0 } &&
        state.CurrentHarvestGrade > 0 &&
        state.HeldGardenCount == butler.StoredItems.Count &&
        state.ActiveLandGardenSize <= state.LandGardenSize &&
        state.ActiveWaterGardenSize <= state.WaterGardenSize;

    private bool HasCoherentStoredItems(
        Character character,
        CharacterButler butler,
        ItemContainer system)
    {
        foreach (var stored in butler.StoredItems.Values)
        {
            var item = _itemManager.GetItemByItemId(stored.ItemId);
            if (stored.Type != GardenLocationType || item == null || item.Count != 1 ||
                item.OwnerId != character.Id || item.SlotType != SlotType.System ||
                !ReferenceEquals(item._holdingContainer, system) || !system.Items.Contains(item))
                return false;
        }
        return true;
    }

    private static bool CanRemoveGarden(ButlerGardenStorageItem garden, ButlerGardenStorageState state)
    {
        if (state.HeldGardenCount == 0)
            return false;
        if (garden.IsUnderWater)
            return state.WaterGardenSize >= garden.GardenSize &&
                   state.WaterGardenSize - garden.GardenSize >= state.ActiveWaterGardenSize;
        return state.LandGardenSize >= garden.GardenSize &&
               state.LandGardenSize - garden.GardenSize >= state.ActiveLandGardenSize;
    }

    private static ButlerItemStorageResult Succeeded() =>
        new(true, true, ButlerItemStorageFailure.None, ErrorMessageType.NoErrorMessage);

    private static ButlerItemStorageResult Failed(ButlerItemStorageFailure failure, ErrorMessageType error) =>
        new(false, false, failure, error);
}
