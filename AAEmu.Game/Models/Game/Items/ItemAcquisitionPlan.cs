using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

public readonly record struct ItemAcquisitionRequest(uint TemplateId, int Count, byte Grade);

/// <summary>
/// A bag credit planned without changing live container state. The caller writes the snapshots on
/// its transaction, commits, and then applies the exact rows while retaining the inventory lease.
/// </summary>
public sealed class ItemAcquisitionPlan : IDisposable
{
    private readonly Inventory _inventory;
    private readonly ItemContainer _container;
    private readonly IItemManager _itemManager;
    private readonly IReadOnlyList<PlannedAcquisition> _entries;
    private IReadOnlyList<ItemPersistenceSnapshot> _snapshots;
    private int _committed;
    private int _applied;
    private int _disposed;

    private ItemAcquisitionPlan(Inventory inventory, ItemContainer container, IItemManager itemManager,
        IReadOnlyList<PlannedAcquisition> entries)
    {
        _inventory = inventory;
        _container = container;
        _itemManager = itemManager;
        _entries = entries;
    }

    internal static bool TryCreate(Inventory inventory, IItemManager itemManager,
        IEnumerable<ItemAcquisitionRequest> requests, DateTime utcNow, out ItemAcquisitionPlan plan)
    {
        plan = null;
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(itemManager);
        if (!Monitor.IsEntered(inventory.MutationSyncRoot) || inventory.Bag == null)
            throw new InvalidOperationException("The inventory mutation lease must be held while planning item acquisition.");

        var bag = inventory.Bag;
        var entries = new List<PlannedAcquisition>();
        var created = new List<Item>();
        var reservedSlots = bag.Items.Where(item => item != null).Select(item => item.Slot).ToHashSet();
        try
        {
            var normalizedRequests = new List<ItemAcquisitionRequest>();
            foreach (var request in requests)
            {
                if (request.TemplateId == 0 || request.Count <= 0 ||
                    ItemWalletRules.ShouldCreditOnAcquire(request.TemplateId, bag.ContainerType, true))
                    return false;
                var template = itemManager.GetTemplate(request.TemplateId);
                if (template == null || template.MaxCount <= 0)
                    return false;
                var grade = template.FixedGrade >= 0 && !template.Gradable
                    ? checked((byte)template.FixedGrade)
                    : request.Grade;
                normalizedRequests.Add(request with { Grade = grade });
            }

            foreach (var request in normalizedRequests
                         .GroupBy(request => (request.TemplateId, request.Grade))
                         .Select(group => new ItemAcquisitionRequest(group.Key.TemplateId,
                             checked(group.Sum(entry => entry.Count)), group.Key.Grade)))
            {
                var template = itemManager.GetTemplate(request.TemplateId);
                var grade = request.Grade;
                var remaining = request.Count;
                foreach (var item in bag.Items
                             .Where(item => item != null && item.TemplateId == request.TemplateId && item.Grade == grade &&
                                            item.HasDefaultDetail && item.MadeUnitId == 0)
                             .OrderBy(item => item.Slot).ThenBy(item => item.Id))
                {
                    if (item.OwnerId != bag.OwnerId || !ReferenceEquals(item._holdingContainer, bag) ||
                        item.SlotType != SlotType.Inventory || item.Count <= 0 || item.Count >= template.MaxCount)
                        continue;
                    var amount = Math.Min(template.MaxCount - item.Count, remaining);
                    entries.Add(new PlannedAcquisition(item, amount, false, item.Slot, []));
                    remaining -= amount;
                    if (remaining == 0)
                        break;
                }

                while (remaining > 0)
                {
                    var slot = FindFreeSlot(bag, reservedSlots);
                    if (slot < 0)
                        return false;
                    var amount = Math.Min(template.MaxCount, remaining);
                    var item = itemManager.Create(request.TemplateId, amount, grade);
                    if (item == null)
                        return false;
                    created.Add(item);
                    reservedSlots.Add(slot);
                    var syncPackets = InitializeNewItem(item, utcNow);
                    entries.Add(new PlannedAcquisition(item, amount, true, slot, syncPackets));
                    remaining -= amount;
                }
            }

            if (entries.Count == 0)
                return false;
            plan = new ItemAcquisitionPlan(inventory, bag, itemManager, entries);
            created.Clear();
            return true;
        }
        finally
        {
            foreach (var item in created)
                itemManager.ReleaseId(item.Id);
        }
    }

    public IReadOnlyList<ItemPersistenceSnapshot> CapturePersistenceSnapshots()
    {
        EnsureLease();
        if (_snapshots != null)
            return _snapshots;
        ValidateLiveState();
        _snapshots = _entries.Select(entry => entry.IsNew
            ? _itemManager.CapturePersistenceSnapshot(entry.Item).MoveTo(_container, entry.Slot)
            : _itemManager.CapturePersistenceSnapshot(entry.Item).IncreaseCountBy(entry.Amount)).ToArray();
        return _snapshots;
    }

    public ItemAcquisitionPublication ApplyCommitted(ItemTaskType taskType)
    {
        EnsureLease();
        if (_snapshots == null)
            throw new InvalidOperationException("Capture and persist this plan before applying it.");
        if (Volatile.Read(ref _committed) == 0)
            throw new InvalidOperationException("Mark the item acquisition transaction committed before applying it.");
        ValidateLiveState();
        if (Interlocked.Exchange(ref _applied, 1) != 0)
            throw new InvalidOperationException("An item acquisition plan can only be applied once.");

        var tasks = new List<ItemTask>(_entries.Count);
        var callbacks = new List<(Item Item, int Amount, bool IsNew)>(_entries.Count);
        var syncPackets = new List<GamePacket>();
        for (var index = 0; index < _entries.Count; index++)
        {
            var entry = _entries[index];
            _itemManager.ApplyCommittedSnapshot(_snapshots[index]);
            tasks.Add(entry.IsNew
                ? new ItemAdd(entry.Item)
                : new ItemCountUpdate(entry.Item.SlotType, checked((byte)entry.Item.Slot), entry.Item.Id,
                    entry.Amount, entry.Item.TemplateId));
            callbacks.Add((entry.Item, entry.Amount, entry.IsNew));
            syncPackets.AddRange(entry.SyncPackets);
        }
        _container.UpdateFreeSlotCount();
        return new ItemAcquisitionPublication(_inventory, _container, taskType, tasks, syncPackets, callbacks);
    }

    /// <summary>
    /// Transfers ownership of newly allocated item IDs to the committed database rows. Once set,
    /// disposal will never release those IDs even if later live publication fails.
    /// </summary>
    public void MarkCommitted()
    {
        EnsureLease();
        if (_snapshots == null)
            throw new InvalidOperationException("Capture and persist this plan before committing it.");
        Volatile.Write(ref _committed, 1);
    }

    private void ValidateLiveState()
    {
        var occupiedSlots = _container.Items.Where(item => item != null).Select(item => item.Slot).ToHashSet();
        foreach (var entry in _entries)
        {
            if (entry.IsNew)
            {
                if (entry.Item._holdingContainer != null || entry.Item.OwnerId != 0 || occupiedSlots.Contains(entry.Slot))
                    throw new InvalidOperationException($"Planned item {entry.Item.Id} is no longer detached or its destination is occupied.");
            }
            else if (!ReferenceEquals(entry.Item._holdingContainer, _container) ||
                     !_container.Items.Contains(entry.Item) || entry.Item.OwnerId != _container.OwnerId ||
                     entry.Item.Count + entry.Amount > entry.Item.Template.MaxCount)
            {
                throw new InvalidOperationException($"Existing item {entry.Item.Id} changed after acquisition was planned.");
            }
        }
    }

    private void EnsureLease()
    {
        if (!Monitor.IsEntered(_inventory.MutationSyncRoot))
            throw new InvalidOperationException("The inventory mutation lease must be held for item acquisition.");
    }

    private static int FindFreeSlot(ItemContainer bag, HashSet<int> reserved)
    {
        var limit = bag.ContainerSize < 0 ? int.MaxValue : bag.ContainerSize;
        for (var slot = 0; slot < limit; slot++)
            if (!reserved.Contains(slot))
                return slot;
        return -1;
    }

    private static IReadOnlyList<GamePacket> InitializeNewItem(Item item, DateTime utcNow)
    {
        var packets = new List<GamePacket>(2);
        if (item.Template.ExpAbsLifetime > 0)
        {
            item.ExpirationTime = utcNow.AddMinutes(item.Template.ExpAbsLifetime);
            packets.Add(new SCSyncItemLifespanPacket(true, item.Id, item.TemplateId, item.ExpirationTime));
        }
        if (item.Template.ExpOnlineLifetime > 0)
        {
            item.ExpirationOnlineMinutesLeft = item.Template.ExpOnlineLifetime;
            packets.Add(new SCSyncItemLifespanPacket(true, item.Id, item.TemplateId,
                utcNow.AddMinutes(item.Template.ExpOnlineLifetime)));
        }
        if (item.Template.ExpDate > DateTime.MinValue)
        {
            item.ExpirationTime = item.Template.ExpDate;
            packets.Add(new SCSyncItemLifespanPacket(true, item.Id, item.TemplateId, item.ExpirationTime));
        }
        if (item is EquipItem equip && item.Template is EquipItemTemplate template)
        {
            equip.ChargeCount = template.ChargeCount;
            if (template.ChargeLifetime > 0 && !template.BindType.HasFlag(ItemBindType.BindOnUnpack))
                equip.ChargeStartTime = utcNow;
        }
        return packets;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0 || Volatile.Read(ref _committed) != 0)
            return;
        foreach (var entry in _entries.Where(entry => entry.IsNew))
            _itemManager.ReleaseId(entry.Item.Id);
    }

    private sealed record PlannedAcquisition(Item Item, int Amount, bool IsNew, int Slot,
        IReadOnlyList<GamePacket> SyncPackets);
}

public sealed class ItemAcquisitionPublication(
    Inventory inventory,
    ItemContainer container,
    ItemTaskType taskType,
    IReadOnlyList<ItemTask> tasks,
    IReadOnlyList<GamePacket> syncPackets,
    IReadOnlyList<(Item Item, int Amount, bool IsNew)> callbacks)
{
    private int _packetsPublished;
    private int _callbacksPublished;

    public void PublishPackets()
    {
        if (!Monitor.IsEntered(inventory.MutationSyncRoot))
            throw new InvalidOperationException("The inventory mutation lease must be held while queuing committed item packets.");
        if (Interlocked.Exchange(ref _packetsPublished, 1) != 0)
            throw new InvalidOperationException("Committed acquisition packets can only be published once.");
        if (tasks.Count > 0)
            inventory.Owner?.SendPacket(new SCItemTaskSuccessPacket(taskType, tasks.ToList(), []));
        foreach (var packet in syncPackets)
            inventory.Owner?.SendPacket(packet);
    }

    public void PublishCallbacks()
    {
        if (Monitor.IsEntered(inventory.MutationSyncRoot))
            throw new InvalidOperationException("Release the inventory mutation lease before publishing item callbacks.");
        if (Volatile.Read(ref _packetsPublished) == 0)
            throw new InvalidOperationException("Committed acquisition packets must be queued before callbacks.");
        if (Interlocked.Exchange(ref _callbacksPublished, 1) != 0)
            throw new InvalidOperationException("Committed acquisition callbacks can only be published once.");
        foreach (var (item, amount, isNew) in callbacks)
        {
            if (isNew)
                container.OnEnterContainer(item, null, 0);
            inventory.OnAcquiredItem(item, amount, !isNew);
        }
    }
}
