using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Pushes one item's detail blob to the client.
/// </summary>
/// <remarks>
/// <para>Wire shape:</para>
/// <code>
/// u64 id          // item id
/// u8  slotType
/// u8  slot
/// detail          // the detail exactly as the full item body carries it, leading detailType byte
///                 // included; written raw, with no length prefix and no padding
/// </code>
/// <para>
/// This is the supported way to publish a detail that changed. The <c>UpdateDetail</c> item task
/// looks like the obvious alternative and is not one: it carries a length-prefixed array whose
/// contents the client does not decode as a detail, which leaves the item in a state that does not
/// match the server's - a broken icon and no tooltip until a relog resends the item.
/// </para>
/// <para>
/// Because the detail is written raw, its field order is this packet's contract: a change to
/// <see cref="Item.WriteDetails"/> changes this packet too.
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
