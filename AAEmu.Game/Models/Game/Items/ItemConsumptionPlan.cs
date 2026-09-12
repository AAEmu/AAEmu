using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Containers;

namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// One immutable selection in a planned stack consumption.
/// </summary>
public sealed class ItemConsumptionEntry
{
    internal ItemConsumptionEntry(Item item, int expectedCount, int remainingCount)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        ExpectedCount = expectedCount;
        RemainingCount = remainingCount;
        ItemId = item.Id;
        OwnerId = item.OwnerId;
        Container = item._holdingContainer;
        ContainerId = Container?.ContainerId ?? 0;
        SlotType = item.SlotType;
        Slot = item.Slot;
        TemplateId = item.TemplateId;
    }

    public Item Item { get; }
    public ulong ItemId { get; }
    public ulong OwnerId { get; }
    public ItemContainer Container { get; }
    public ulong ContainerId { get; }
    public SlotType SlotType { get; }
    public int Slot { get; }
    public uint TemplateId { get; }
    public int ExpectedCount { get; }
    public int RemainingCount { get; }
    public int DebitCount => ExpectedCount - RemainingCount;

    internal bool MatchesLiveState(ItemContainer expectedContainer) =>
        ReferenceEquals(Container, expectedContainer) &&
        ReferenceEquals(Item._holdingContainer, expectedContainer) &&
        Item.Id == ItemId &&
        Item.OwnerId == OwnerId &&
        Item.TemplateId == TemplateId &&
        Item.SlotType == SlotType &&
        Item.Slot == Slot &&
        Item.Count == ExpectedCount;
}

/// <summary>
/// A bag debit selected without changing live state. The caller persists matching item snapshots,
/// commits its transaction, and only then applies this plan while retaining the inventory lease.
/// </summary>
public sealed class ItemConsumptionPlan
{
    private readonly Inventory _inventory;
    private readonly ItemContainer _container;
    private readonly IReadOnlyList<ItemConsumptionEntry> _entries;
    private IItemManager _itemManager;
    private IReadOnlyList<ItemPersistenceSnapshot> _persistenceSnapshots;
    private int _applied;

    internal ItemConsumptionPlan(
        Inventory inventory,
        ItemContainer container,
        uint templateId,
        int requestedCount,
        IReadOnlyList<ItemConsumptionEntry> entries)
    {
        _inventory = inventory;
        _container = container;
        TemplateId = templateId;
        RequestedCount = requestedCount;
        _entries = entries;
    }

    public uint TemplateId { get; }
    public int RequestedCount { get; }
    public IReadOnlyList<ItemConsumptionEntry> Entries => _entries;

    /// <summary>
    /// Captures the exact rows this plan will persist. The returned snapshots are retained so the
    /// same committed images are applied to live objects.
    /// </summary>
    public IReadOnlyList<ItemPersistenceSnapshot> CapturePersistenceSnapshots(IItemManager itemManager)
    {
        ArgumentNullException.ThrowIfNull(itemManager);
        if (!Monitor.IsEntered(_inventory.MutationSyncRoot))
            throw new InvalidOperationException("The inventory mutation lease must be held while capturing item snapshots");
        if (_persistenceSnapshots != null)
        {
            if (!ReferenceEquals(_itemManager, itemManager))
                throw new InvalidOperationException("Item snapshots were already captured by a different item manager");
            return _persistenceSnapshots;
        }

        foreach (var entry in _entries)
        {
            if (!entry.MatchesLiveState(_container))
                throw new InvalidOperationException($"Item {entry.ItemId} changed after its consumption was planned");
        }
        _itemManager = itemManager;
        _persistenceSnapshots = _entries
            .Select(entry =>
            {
                var snapshot = itemManager.CapturePersistenceSnapshot(entry.Item);
                return entry.RemainingCount == 0
                    ? snapshot.Delete()
                    : snapshot.WithCount(entry.RemainingCount);
            })
            .ToArray();
        return _persistenceSnapshots;
    }

    /// <summary>
    /// Applies the already-committed live delta. Packet and quest publication is returned to the
    /// caller so it can release its database, Butler, and inventory guards first.
    /// </summary>
    public ItemConsumptionPublication ApplyCommitted(ItemTaskType taskType)
    {
        if (!Monitor.IsEntered(_inventory.MutationSyncRoot))
            throw new InvalidOperationException("The inventory mutation lease must be held while applying a committed item plan");
        if (_persistenceSnapshots == null)
            throw new InvalidOperationException("Capture and persist this item plan's snapshots before applying it");
        if (Interlocked.Exchange(ref _applied, 1) != 0)
            throw new InvalidOperationException("An item consumption plan can only be applied once");

        foreach (var entry in _entries)
        {
            if (!entry.MatchesLiveState(_container))
                throw new InvalidOperationException($"Item {entry.ItemId} changed after its consumption was planned");
        }
        foreach (var snapshot in _persistenceSnapshots)
            snapshot.ValidateLiveState();

        var itemTasks = new List<ItemTask>(_entries.Count);
        var forceRemove = new List<ulong>();
        var expirationPackets = new List<GamePacket>();
        var consumed = new List<(Item Item, int Count, byte PreviousSlot)>();

        for (var index = 0; index < _entries.Count; index++)
        {
            var entry = _entries[index];
            var snapshot = _persistenceSnapshots[index];
            if (entry.RemainingCount > 0)
            {
                _itemManager.ApplyCommittedSnapshot(snapshot);
                itemTasks.Add(new ItemCountUpdate(
                    entry.SlotType,
                    checked((byte)entry.Slot),
                    entry.ItemId,
                    -entry.DebitCount,
                    entry.TemplateId));
            }
            else
            {
                var expirationPacket = ItemManager.ExpireItemPacket(entry.Item);
                if (expirationPacket != null)
                    expirationPackets.Add(expirationPacket);

                itemTasks.Add(new ItemRemoveSlot(entry.Item));
                forceRemove.Add(entry.ItemId);
                _itemManager.FinalizeCommittedRemoval(entry.Item);
            }

            consumed.Add((entry.Item, entry.DebitCount, checked((byte)entry.Slot)));
        }

        _container.UpdateFreeSlotCount();
        return new ItemConsumptionPublication(
            _inventory,
            _container,
            taskType,
            itemTasks,
            forceRemove,
            expirationPackets,
            consumed);
    }
}

/// <summary>
/// Deferred observable side effects of an already-committed item debit.
/// </summary>
public sealed class ItemConsumptionPublication
{
    private readonly Inventory _inventory;
    private readonly ItemContainer _container;
    private readonly ItemTaskType _taskType;
    private readonly List<ItemTask> _itemTasks;
    private readonly List<ulong> _forceRemove;
    private readonly List<GamePacket> _expirationPackets;
    private readonly List<(Item Item, int Count, byte PreviousSlot)> _consumed;
    private int _packetsPublished;
    private int _callbacksPublished;

    internal ItemConsumptionPublication(
        Inventory inventory,
        ItemContainer container,
        ItemTaskType taskType,
        List<ItemTask> itemTasks,
        List<ulong> forceRemove,
        List<GamePacket> expirationPackets,
        List<(Item Item, int Count, byte PreviousSlot)> consumed)
    {
        _inventory = inventory;
        _container = container;
        _taskType = taskType;
        _itemTasks = itemTasks;
        _forceRemove = forceRemove;
        _expirationPackets = expirationPackets;
        _consumed = consumed;
    }

    /// <summary>
    /// Queues the committed item packets while the inventory mutation lease still prevents a
    /// later move from overtaking them on the connection's ordered send path.
    /// </summary>
    public void PublishPackets()
    {
        if (!Monitor.IsEntered(_inventory.MutationSyncRoot))
            throw new InvalidOperationException("The inventory mutation lease must be held while queuing committed item packets");
        if (Interlocked.Exchange(ref _packetsPublished, 1) != 0)
            throw new InvalidOperationException("Committed item packets can only be published once");

        foreach (var packet in _expirationPackets)
            _inventory.Owner?.SendPacket(packet);

        if (_taskType != ItemTaskType.Invalid && _itemTasks.Count > 0)
            _inventory.Owner?.SendPacket(new SCItemTaskSuccessPacket(_taskType, _itemTasks, _forceRemove));
    }

    /// <summary>
    /// Runs quest and container callbacks after the inventory and persistence guards are released.
    /// </summary>
    public void PublishCallbacks()
    {
        if (Monitor.IsEntered(_inventory.MutationSyncRoot))
            throw new InvalidOperationException("Release the inventory mutation lease before publishing item callbacks");
        if (Volatile.Read(ref _packetsPublished) == 0)
            throw new InvalidOperationException("Committed item packets must be queued before publishing callbacks");
        if (Interlocked.Exchange(ref _callbacksPublished, 1) != 0)
            throw new InvalidOperationException("Committed item callbacks can only be published once");

        foreach (var (item, count, previousSlot) in _consumed)
        {
            _inventory.OnConsumedItem(item, count);
            if (item._holdingContainer == null)
                _container.OnLeaveContainer(item, null, previousSlot);
        }
    }
}
