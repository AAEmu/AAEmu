using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMateEquipmentChangedPacket : GamePacket
    {
        private readonly ushort _mateTlId;
        private readonly uint _characterId;
        private readonly uint _passengerId;
        private readonly bool _bts;
        private readonly byte _num;
        private readonly bool _success;
        private readonly ItemAndLocation _itemOnPet;
        private readonly ItemAndLocation _itemInBag;
        private readonly DateTime _expireTime;

        public SCMateEquipmentChangedPacket(ItemAndLocation itemOnPet, ItemAndLocation itemInBag, ushort mateTlId, uint characterId, uint passengerId, bool bts, bool success, DateTime expireTime)
            : base(SCOffsets.SCMateEquipmentChangedPacket, 5)
        {
            _itemOnPet = itemOnPet;
            _itemInBag = itemInBag;
            _mateTlId = mateTlId;
            _characterId = characterId;
            _passengerId = passengerId;
            _bts = bts;
            _num = 1; // all time == 1
            _success = success;
            _expireTime = expireTime;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_characterId); // type
            stream.Write(_mateTlId);   // tl
            stream.Write(_passengerId); // type
            stream.Write(_bts); // bts
            stream.Write(_num); // num

            if (_itemOnPet.Item == null)
                stream.Write(0);
            else
                stream.Write(_itemOnPet.Item);

            if (_itemInBag.Item == null)
                stream.Write(0);
            else
                stream.Write(_itemInBag.Item);

            stream.Write((byte)_itemOnPet.SlotType);
            stream.Write(_itemOnPet.SlotNumber);
            stream.Write((byte)_itemInBag.SlotType);
            stream.Write(_itemInBag.SlotNumber);
            stream.Write(_expireTime); // add in 5+
            stream.Write(_success); // success

            return stream;
        }
    }
}
