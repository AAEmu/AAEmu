namespace AAEmu.Game.Models.Game.Crafts;

/// <summary>
/// A live craft order: what one character asked another to craft, and the fee they escrowed for it.
/// Rows are written to <c>craft_orders</c> as soon as they are posted, cancelled, filled, or they
/// lapse.
/// </summary>
public sealed class CraftOrder
{
    /// <summary>Order id. Also what the cancel request and the delete packet carry.</summary>
    public ulong Id { get; init; }

    public uint OwnerId { get; init; }
    public string OwnerName { get; init; } = string.Empty;

    /// <summary>
    /// Owner id as the board row stores it. Complete compares this to the local character id, so
    /// it must be that id and not a world-tagged composite.
    /// </summary>
    public ulong OwnerWorldCharKey { get; init; }

    /// <summary>Craft the order asks for.</summary>
    public uint CraftId { get; init; }

    /// <summary>Item the craft produces, which is what the board lists.</summary>
    public uint ItemId { get; init; }

    public byte Grade { get; init; }
    public uint Count { get; init; }

    /// <summary>Fee offered, in copper, held while the order is live.</summary>
    public ulong Fee { get; init; }

    public uint ActabilityGroupId { get; init; }

    /// <summary>Actability the fulfiller needs for this craft.</summary>
    public uint ActabilityPoint { get; init; }

    public long PostedUnix { get; init; }
    public long ExpiresUnix { get; init; }

    /// <summary>Order status, in the client's own id space.</summary>
    public byte Status { get; init; }

    /// <summary>Order kind, in the client's own id space.</summary>
    public byte Kind { get; init; }

    /// <summary>
    /// The row the client reads. Complete compares the owner field to the local character id.
    /// </summary>
    public CraftOrderEntry ToWireEntry()
    {
        return new CraftOrderEntry(
            Id: Id,
            Kind: Kind,
            Unnamed1: CraftOrderProcessRules.InstantOwnerId(OwnerId),
            OrderItemId: ItemId,
            Unnamed2: CraftId,
            CraftCount: Count,
            Unnamed3: Grade,
            MoneyAmount: Fee,
            Unnamed4: ActabilityGroupId,
            ActabilityPoint: ActabilityPoint,
            PostDate: (ulong)Math.Max(0, PostedUnix),
            ExpireDate: (ulong)Math.Max(0, ExpiresUnix),
            Status: Status,
            Unnamed5: 0);
    }
}
