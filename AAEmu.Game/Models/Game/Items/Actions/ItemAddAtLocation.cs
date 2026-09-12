using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

/// <summary>
/// Publishes a full item at a protocol location which differs from its durable server container.
/// Farmhand garden items live in the character's System container while the client addresses the
/// farmhand bag through the Inventory (2), slot-zero namespace sentinel.
/// </summary>
public sealed class ItemAddAtLocation(Item item, SlotType slotType, byte slot) : ItemTask
{
    private readonly Item _item = item ?? throw new ArgumentNullException(nameof(item));

    public override PacketStream Write(PacketStream stream)
    {
        _type = ItemAction.Take;
        base.Write(stream);
        stream.Write((byte)slotType);
        stream.Write(slot);
        WriteDetails(stream, _item);
        return stream;
    }
}
