using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public class ItemMove : ItemTask
{
    private readonly SlotType _fromSlotType;
    private readonly byte _fromSlot;
    private readonly ulong _fromItemId;
    private readonly SlotType _toSlotType;
    private readonly byte _toSlot;
    private readonly ulong _toItemId;

    public ItemMove(SlotType fromSlotType, byte fromSlot, ulong fromItemId, SlotType toSlotType, byte toSlot, ulong toItemId)
    {
        _type = ItemAction.SwapSlot; // 8
        _fromSlotType = fromSlotType;
        _fromSlot = fromSlot;
        _fromItemId = fromItemId;
        _toSlotType = toSlotType;
        _toSlot = toSlot;
        _toItemId = toItemId;
        _tLogt = SetTlogT(_type, fromSlotType == toSlotType ?
            SlotType.Bag : // установим tLogt по значению ItemAction, предмет в одном месте
            SlotType.Bank); // установим tLogt по значению ItemAction, предмет в разных местах
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write((byte)_fromSlotType); // type
        stream.Write(_fromSlot);           // index
        stream.Write((byte)_toSlotType);   // type
        stream.Write(_toSlot);             // index
        stream.Write(_fromItemId); // i1
        stream.Write(_toItemId);   // i2
        stream.Write(0);           //flags
        return stream;
    }
}
