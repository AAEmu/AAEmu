using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

/// <summary>
/// Adds an item to the client farmhand bag without resolving a physical client inventory.
/// The 10.0.2.13 client's item action 0x15 carries both protocol
/// locations, the canonical item, an optional removed farmhand item id, and an unused
/// farmhand database id. The owner-7 handler calls ClientButler directly.
/// </summary>
public sealed class ButlerItemAdd(
    Item item,
    byte sourceType,
    byte sourceIndex,
    byte destinationType,
    byte destinationIndex) : ItemTask
{
    private const byte ButlerOwnerType = 7;
    private readonly Item _item = item ?? throw new ArgumentNullException(nameof(item));

    public override PacketStream Write(PacketStream stream)
    {
        _type = ItemAction.ButlerItemSwap;
        base.Write(stream);
        stream.Write(ButlerOwnerType);
        stream.Write(sourceType);
        stream.Write(sourceIndex);
        stream.Write(destinationType);
        stream.Write(destinationIndex);
        WriteDetails(stream, _item);
        stream.Write((ulong)0); // removeItemId: this action only adds to the farmhand bag
        stream.Write((ulong)0); // dbButlerId: parsed but unused by the owner-7 client handler
        return stream;
    }
}
