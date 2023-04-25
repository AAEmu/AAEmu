using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMateEquipmentChangedPacket : GamePacket
    {

        public readonly Item _itemA;
        public readonly Item _itemB;
        public readonly SlotType _typeA;
        public readonly SlotType _typeB;
        public readonly byte _slotA;
        public readonly byte _slotB;
        public readonly ushort _tlId;

        public SCMateEquipmentChangedPacket(Item itemA, SlotType typeA, byte slotA, Item itemB, SlotType typeB, byte slotB, ushort tlId) : base(SCOffsets.SCMateEquipmentChangedPacket, 1)
        {
            _itemA = itemA;
            _itemB = itemB;
            _typeA = typeA;
            _typeB = typeB;
            _slotA = slotA;
            _slotB = slotB;
            _tlId = tlId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(1);
            stream.Write(_tlId);
            stream.Write(1);
            stream.Write((byte)0);
            stream.Write((byte)1);

            if (_itemA == null || _itemA.TemplateId == 0)
                stream.Write(0);
            else
                stream.Write(_itemA);

            if (_itemB == null || _itemB.TemplateId == 0)
                stream.Write(0);
            else
                stream.Write(_itemB);

            stream.Write((byte)_typeA);
            stream.Write(_slotA);
            stream.Write((byte)_typeB);
            stream.Write(_slotB);

            if (_itemB == null || _itemB.TemplateId == 0)
                stream.Write(0);
            else
                stream.Write(_itemB);

            if (_itemA == null || _itemA.TemplateId == 0)
                stream.Write(0);
            else
                stream.Write(_itemA);

            stream.Write((byte)_typeB);
            stream.Write(_slotB);
            stream.Write((byte)_typeA);
            stream.Write(_slotA);

            return stream;
        }
    }
}
