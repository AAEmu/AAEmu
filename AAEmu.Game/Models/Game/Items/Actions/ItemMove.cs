using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
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
            _type = ItemAction.SwapSlot;
            _fromSlotType = fromSlotType;
            _fromSlot = fromSlot;
            _fromItemId = fromItemId;
            _toSlotType = toSlotType;
            _toSlot = toSlot;
            _toItemId = toItemId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);

            stream.Write((byte)_fromSlotType);
            stream.Write(_fromSlot);

            stream.Write((byte)_toSlotType);
            stream.Write(_toSlot);

            stream.Write(_fromItemId);
            stream.Write(_toItemId); // i2

            stream.Write(0); //flags
            return stream;
        }
    }
}
