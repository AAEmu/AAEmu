using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSlaveStatePacket : GamePacket
    {
        private readonly uint _objId;
        private readonly ushort _tlId;
        private readonly int _skillCount = 0;
        private readonly int _tagCount = 0;
        private readonly string _creatorName;
        private readonly uint _ownerId;
        private readonly uint _dbId;

        public SCSlaveStatePacket(uint objId, ushort tlId, string creatorName, uint ownerId, uint dbId) :
            base(SCOffsets.SCSlaveStatePacket, 5)
        {
            _objId = objId;
            _tlId = tlId;
            _creatorName = creatorName;
            _dbId = dbId;
            _ownerId = ownerId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.Write(_tlId);
            stream.Write(0ul); // type
            stream.Write(_skillCount);
            stream.Write(_tagCount);
            stream.Write(_creatorName);
            stream.Write(_ownerId);
            stream.Write(_dbId);
            return stream;
        }
    }
}
