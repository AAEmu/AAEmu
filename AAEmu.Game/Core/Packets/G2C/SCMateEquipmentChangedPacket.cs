using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMateEquipmentChangedPacket : GamePacket
    {
        private readonly ushort _tlId;
        private readonly uint _characterId;
        private readonly uint _passengerId;
        private readonly bool _bts;
        private readonly byte _num;
        private readonly (SlotType type, byte slot, Item item) _itemA;
        private readonly (SlotType type, byte slot, Item item) _itemB;

        public SCMateEquipmentChangedPacket((SlotType type, byte slot, Item item) itemA, (SlotType type, byte slot, Item item) itemB, ushort tlId, uint characterId, uint passengerId, bool bts, byte num) : base(SCOffsets.SCMateEquipmentChangedPacket, 1)
        {
            _itemA = itemA;
            _itemB = itemB;
            _tlId = tlId;
            _characterId = characterId;
            _passengerId = passengerId;
            _bts = bts;
            _num = num;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_characterId); // type
            stream.Write(_tlId);        // tl
            stream.Write(_passengerId); // type
            stream.Write(_bts);         // bts
            stream.Write(_num);         // num

            for (var i = 0; i < _num; i++)
            {
                if (_itemA.item?.TemplateId == null)
                    stream.Write(0);
                else
                    stream.Write(_itemA.item);

                if (_itemB.item?.TemplateId == null)
                    stream.Write(0);
                else
                    stream.Write(_itemB.item);

                stream.Write((byte)_itemA.type);
                stream.Write(_itemA.slot);
                stream.Write((byte)_itemB.type);
                stream.Write(_itemB.slot);
            }

            stream.Write(true); // success

            return stream;
        }
    }
}
