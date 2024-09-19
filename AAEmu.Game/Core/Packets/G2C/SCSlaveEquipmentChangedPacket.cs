using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSlaveEquipmentChangedPacket : GamePacket
    {
        private readonly ushort _slaveTlId;
        private readonly uint _characterId;
        private readonly uint _dbSlaveId;
        private readonly bool _bts;
        private readonly byte _num;
        private readonly bool _success;
        private readonly ItemAndLocation _itemOnSlave;
        private readonly ItemAndLocation _itemInBag;
        private readonly DateTime _expireTime;

        public SCSlaveEquipmentChangedPacket(ItemAndLocation itemOnSlave, ItemAndLocation itemInBag, ushort slaveTlId, uint characterId, uint dbSlaveId, bool bts, bool success, DateTime expireTime)
            : base(SCOffsets.SCSlaveEquipmentChangedPacket, 5)
        {
            _itemOnSlave = itemOnSlave;
            _itemInBag = itemInBag;
            _slaveTlId = slaveTlId;
            _characterId = characterId;
            _dbSlaveId = dbSlaveId;
            _bts = bts;
            _num = 1; // all time == 1
            _success = success;
            _expireTime = expireTime;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_characterId); // type
            stream.Write(_slaveTlId); // tl
            stream.Write(_dbSlaveId); // type
            stream.Write(_bts); // bts
            stream.Write(_num); // num

            if (_itemOnSlave.Item == null)
                stream.Write(0);
            else
                stream.Write(_itemOnSlave.Item);

            if (_itemInBag.Item == null)
                stream.Write(0);
            else
                stream.Write(_itemInBag.Item);

            stream.Write((byte)_itemOnSlave.SlotType);
            stream.Write(_itemOnSlave.SlotNumber);
            stream.Write((byte)_itemInBag.SlotType);
            stream.Write(_itemInBag.SlotNumber);
            stream.Write(_expireTime); // add in 5+
            stream.Write(_success); // success

            return stream;
        }
    }
}
