using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSyncItemLifespanPacket : GamePacket
    {
        private readonly bool _added;
        private readonly ulong _itemId;
        private readonly uint _itemTemplateId;
        private readonly DateTime _expireTime;

        public SCSyncItemLifespanPacket(bool added, ulong itemId, uint itemTemplateId, DateTime expireTime) : base(SCOffsets.SCSyncItemLifespanPacket, 1)
        {
            _added = added;
            _itemId = itemId;
            _itemTemplateId = itemTemplateId;
            _expireTime = expireTime;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_added);
            stream.Write(_itemId);
            stream.Write(_itemTemplateId);
            stream.Write(_expireTime);

            return stream;
        }
    }
}
