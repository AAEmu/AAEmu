using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSlaveCreatedPacket : GamePacket
    {
        private readonly uint _ownerObjId;
        private readonly ushort _tlId;
        private readonly uint _slaveObjId;
        private readonly bool _hideSpawnEffect;
        private readonly long _unkId;
        private readonly string _creatorName;
        
        public SCSlaveCreatedPacket(uint ownerObjId, ushort tlId, uint slaveObjId, bool hideSpawnEffect, long unkId, string creatorName)
            : base(SCOffsets.SCSlaveCreatedPacket, 5)
        {
            _ownerObjId = ownerObjId;
            _tlId = tlId;
            _slaveObjId = slaveObjId;
            _hideSpawnEffect = hideSpawnEffect;
            _unkId = unkId;
            _creatorName = creatorName;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_ownerObjId);
            stream.Write(_tlId);
            stream.WriteBc(_slaveObjId);
            stream.Write(_hideSpawnEffect);
            stream.Write(_unkId);
            stream.Write(_creatorName);
            return stream;
        }
    }
}
