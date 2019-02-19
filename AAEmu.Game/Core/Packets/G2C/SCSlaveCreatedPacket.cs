using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSlaveCreatedPacket : GamePacket
    {
        private readonly uint _objId;
        private readonly ushort _tl;
        private readonly uint _obj2Id;
        private readonly bool _hideSpawnEffect;
        private readonly long _unkId;
        private readonly string _creatorName;
        
        public SCSlaveCreatedPacket(uint objId, ushort tl, uint obj2Id, bool hideSpawnEffect, long unkId, string creatorName)
            : base(0x060, 1)
        {
            _objId = objId;
            _tl = tl;
            _obj2Id = obj2Id;
            _hideSpawnEffect = hideSpawnEffect;
            _unkId = unkId;
            _creatorName = creatorName;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.Write(_tl);
            stream.WriteBc(_obj2Id);
            stream.Write(_hideSpawnEffect);
            stream.Write(_unkId);
            stream.Write(_creatorName);
            return stream;
        }
    }
}
