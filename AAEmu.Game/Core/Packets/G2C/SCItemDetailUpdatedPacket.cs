using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Pushes one item's detail blob to the client, opcode 0xBE.
/// </summary>
/// <remarks>
/// <para>Schema:</para>
/// <code>
/// u64 id          // item id
/// u8  slotType
/// u8  slot
/// detail          // the item detail exactly as the full item body carries it, leading detailType
///                 // byte included; written raw, with no length prefix and no padding
/// </code>
/// <para>
/// This is the supported way to publish a changed detail. The <c>UpdateDetail</c> item task carries a
/// length-prefixed array instead, whose contents are not decoded as a detail by the receiver - see
/// <see cref="AAEmu.Game.Models.Game.Items.Actions.ItemUpdate"/>. Because the detail here is written
/// raw, its field order is the contract: a change to the detail serializer changes this packet too,
/// which is what a round-trip test over the whole body is there to catch.
/// </para>
/// </remarks>
public class SCItemDetailUpdatedPacket(Item item) : GamePacket(SCOffsets.SCItemDetailUpdatedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(item.Id);
        stream.Write((byte)item.SlotType);
        stream.Write((byte)item.Slot);
        stream.Write((byte)item.DetailType);
        item.WriteDetails(stream);
        return stream;
    }
}
