using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items.Containers;
using System.Collections.Immutable;

namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// An immutable before-and-after image of one persisted item row.  Callers build it while
/// holding their inventory guard, write it on their own transaction, then apply it only after
/// that transaction commits.
/// </summary>
public sealed record ItemPersistenceSnapshot
{
    private ItemPersistenceSnapshot(Item item, ItemPersistenceRow expected, ItemPersistenceRow desired,
        ItemContainer expectedContainer, ItemContainer destinationContainer)
    {
        Item = item;
        Expected = expected;
        Desired = desired;
        ExpectedContainer = expectedContainer;
        DestinationContainer = destinationContainer;
    }

    public Item Item { get; }
    public ItemPersistenceRow Expected { get; }
    public ItemPersistenceRow Desired { get; private init; }
    public ItemContainer ExpectedContainer { get; }
    public ItemContainer DestinationContainer { get; private init; }
    public bool DeletesItem => Desired.Count == 0;

    public static ItemPersistenceSnapshot Capture(Item item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var row = ItemPersistenceRow.Capture(item);
        return new ItemPersistenceSnapshot(item, row, row, item._holdingContainer, item._holdingContainer);
    }

    public ItemPersistenceSnapshot WithCount(int count)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Use Delete for a fully consumed stack.");
        if (count > Expected.Count)
            throw new ArgumentOutOfRangeException(nameof(count), "A targeted snapshot cannot increase a source stack.");
        return this with { Desired = Desired with { Count = count } };
    }

    /// <summary>Projects an increase of an existing stack while retaining its exact live-state precondition.</summary>
    public ItemPersistenceSnapshot IncreaseCountBy(int count)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        var desired = checked(Expected.Count + count);
        if (desired > Item.Template.MaxCount)
            throw new ArgumentOutOfRangeException(nameof(count), "The projected stack exceeds its template maximum.");
        return this with { Desired = Desired with { Count = desired } };
    }

    public ItemPersistenceSnapshot Delete() => this with { Desired = Desired with { Count = 0 } };

    public ItemPersistenceSnapshot MoveTo(ItemContainer destination, int slot)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (destination.ContainerId == 0)
            throw new ArgumentException("A targeted item move needs a persistent destination container.", nameof(destination));
        if (!Enum.IsDefined(destination.ContainerType) || destination.ContainerType == SlotType.None)
            throw new ArgumentOutOfRangeException(nameof(destination));
        if (destination.OwnerId == 0)
            throw new ArgumentException("A targeted item move needs a destination owner.", nameof(destination));
        if (slot < 0)
            throw new ArgumentOutOfRangeException(nameof(slot));

        return this with
        {
            Desired = Desired with
            {
                ContainerId = destination.ContainerId,
                SlotType = destination.ContainerType,
                Slot = slot,
                OwnerId = destination.OwnerId
            },
            DestinationContainer = destination
        };
    }

    /// <summary>
    /// Projects an existing item into a caller-owned destination that is not represented by an
    /// <see cref="ItemContainer"/>, such as a mail attachment. The live item remains untouched
    /// until the caller commits and publishes its enclosing transaction.
    /// </summary>
    public ItemPersistenceSnapshot WithLocation(ulong containerId, SlotType slotType, int slot, ulong ownerId)
    {
        if (containerId != 0 || slotType != SlotType.Mail)
            throw new ArgumentException("An uncontained projection is only valid for a mail item row.", nameof(containerId));
        if (!Enum.IsDefined(slotType))
            throw new ArgumentOutOfRangeException(nameof(slotType));
        if (slot < 0)
            throw new ArgumentOutOfRangeException(nameof(slot));
        if (ownerId == 0)
            throw new ArgumentOutOfRangeException(nameof(ownerId));
        return this with
        {
            Desired = Desired with { ContainerId = containerId, SlotType = slotType, Slot = slot, OwnerId = ownerId },
            DestinationContainer = null
        };
    }

    public void ValidateLiveState()
    {
        if (!Expected.Matches(Item) || !ReferenceEquals(ExpectedContainer, Item._holdingContainer))
            throw new InvalidOperationException($"Item {Expected.Id} changed after its persistence snapshot was captured.");
        if (ExpectedContainer != null && !ExpectedContainer.Items.Contains(Item))
            throw new InvalidOperationException($"Item {Expected.Id} is no longer a member of its captured source container.");
    }

    /// <summary>Checks every source/destination invariant before a caller writes any transaction rows.</summary>
    public void ValidateForPersistence()
    {
        ValidateLiveState();
        if (DeletesItem)
            return;
        if (Desired.Count <= 0 || !Enum.IsDefined(Desired.SlotType) || Desired.OwnerId == 0)
            throw new InvalidOperationException($"Item {Expected.Id} has an invalid projected row.");

        if (DestinationContainer == null)
        {
            if (Desired.ContainerId != 0 || Desired.SlotType != SlotType.Mail)
                throw new InvalidOperationException($"Item {Expected.Id} has a detached non-mail destination.");
            return;
        }

        if (DestinationContainer.ContainerId != Desired.ContainerId || DestinationContainer.ContainerType != Desired.SlotType ||
            DestinationContainer.OwnerId != Desired.OwnerId)
            throw new InvalidOperationException($"Item {Expected.Id} destination container changed after snapshot capture.");
        if (Desired.Slot < 0 || (DestinationContainer.ContainerSize >= 0 && Desired.Slot >= DestinationContainer.ContainerSize))
            throw new InvalidOperationException($"Item {Expected.Id} destination slot {Desired.Slot} is outside its container.");
        if (DestinationContainer.Items.Any(existing => !ReferenceEquals(existing, Item) && existing.Slot == Desired.Slot))
            throw new InvalidOperationException($"Item {Expected.Id} destination slot {Desired.Slot} is occupied.");
    }

    public IEnumerable<ItemContainer> PersistentContainers()
    {
        if (ExpectedContainer is { ContainerId: > 0, ContainerType: not SlotType.None })
            yield return ExpectedContainer;
        if (DestinationContainer is { ContainerId: > 0, ContainerType: not SlotType.None } &&
            !ReferenceEquals(DestinationContainer, ExpectedContainer))
            yield return DestinationContainer;
    }
}

/// <summary>All fields persisted in one <c>items</c> row, including the serialized subclass details.</summary>
public sealed record ItemPersistenceRow(
    ulong Id,
    string Type,
    uint TemplateId,
    ulong ContainerId,
    SlotType SlotType,
    int Slot,
    int Count,
    ItemDetailType DetailType,
    ImmutableArray<byte> Details,
    int LifespanMins,
    uint MadeUnitId,
    DateTime UnsecureTime,
    DateTime UnpackTime,
    ulong OwnerId,
    DateTime CreatedAt,
    byte Grade,
    ItemFlag Flags,
    ulong UccId,
    DateTime ExpirationTime,
    double ExpirationOnlineMinutes,
    DateTime ChargeStartTime,
    int ChargeCount)
{
    public static ItemPersistenceRow Capture(Item item)
    {
        var details = new PacketStream();
        item.WriteDetails(details);
        return new ItemPersistenceRow(
            item.Id, item.GetType().ToString(), item.TemplateId, item._holdingContainer?.ContainerId ?? 0,
            item.SlotType, item.Slot, item.Count, item.DetailType, ImmutableArray.CreateRange(details.GetBytes()), item.LifespanMins, item.MadeUnitId,
            item.UnsecureTime, item.UnpackTime, item.OwnerId, item.CreateTime, item.Grade, item.ItemFlags,
            item.UccId, item.ExpirationTime, item.ExpirationOnlineMinutesLeft, item.ChargeStartTime, item.ChargeCount);
    }

    public bool Matches(Item item)
    {
        if (item == null)
            return false;

        var current = Capture(item);
        return Id == current.Id && Type == current.Type && TemplateId == current.TemplateId &&
               ContainerId == current.ContainerId && SlotType == current.SlotType && Slot == current.Slot &&
               Count == current.Count && DetailType == current.DetailType && LifespanMins == current.LifespanMins && MadeUnitId == current.MadeUnitId &&
               UnsecureTime == current.UnsecureTime && UnpackTime == current.UnpackTime && OwnerId == current.OwnerId &&
               CreatedAt == current.CreatedAt && Grade == current.Grade && Flags == current.Flags && UccId == current.UccId &&
               ExpirationTime == current.ExpirationTime && ExpirationOnlineMinutes == current.ExpirationOnlineMinutes &&
               ChargeStartTime == current.ChargeStartTime && ChargeCount == current.ChargeCount &&
               Details.AsSpan().SequenceEqual(current.Details.AsSpan());
    }
}

/// <summary>Immutable persisted metadata for a container referenced by a targeted item row.</summary>
public sealed record ItemContainerPersistenceRow(
    ulong ContainerId,
    string ContainerTypeName,
    SlotType SlotType,
    int ContainerSize,
    uint OwnerId,
    uint MateId,
    ulong ParentItemId)
{
    public static ItemContainerPersistenceRow Capture(ItemContainer container) =>
        new(container.ContainerId, container.ContainerTypeName(), container.ContainerType, container.ContainerSize,
            container.OwnerId, container.MateId,
            container is ItemBagContainer itemBagContainer ? itemBagContainer.ParentItemId : 0);
}
