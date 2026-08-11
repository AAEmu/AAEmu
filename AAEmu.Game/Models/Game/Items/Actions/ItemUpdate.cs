using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

/// <summary>
/// UpdateDetail item task (action 10).
/// </summary>
/// <remarks>
/// Schema: <c>u8 slotType, u8 slot, u64 itemId</c>, then the detail as <c>u16 byteLength</c> followed
/// by that many bytes.
/// <para>
/// <b>Do not use this to publish an item's detail.</b> The framing above is honoured, but the bytes in
/// that array are not decoded as a detail by the receiver, so the item is left in a state that does not
/// match the server's - a broken icon and no tooltip until the next login resends it.
/// <see cref="AAEmu.Game.Core.Packets.G2C.SCItemDetailUpdatedPacket"/> is the supported way to publish
/// a changed detail, and synthesis and repair both use it.
/// </para>
/// <para>
/// This type is kept only for the call sites that predate that conclusion and have not been
/// re-verified. New code should not add to them; the remaining ones should either be moved to
/// SCItemDetailUpdated or established with a test that shows what the receiver does with the array.
/// </para>
/// </remarks>
public class ItemUpdate : ItemTask
{
    private readonly Item _item;

    public ItemUpdate(Item item)
    {
        _type = ItemAction.UpdateDetail;
        _item = item;
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);

        stream.Write((byte)_item.SlotType);
        stream.Write((byte)_item.Slot);

        stream.Write(_item.Id);
        var details = new PacketStream();
        details.Write((byte)_item.DetailType);
        _item.WriteDetails(details);
        stream.Write((short)128);
        stream.Write(details, false);
        stream.Write(new byte[128 - details.Count]);
        return stream;
    }
}
