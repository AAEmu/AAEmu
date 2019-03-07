using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSlaveStatePacket : GamePacket
    {
        private readonly uint _objId;
        private readonly ushort _tlId;
        private readonly ulong _type;
        private readonly int _skillCount = 0;
        private readonly int _tagCount = 0;
        private readonly string _creatorName;
        private readonly uint _type2 = 0;
        private readonly uint _dbId;
        
        public SCSlaveStatePacket(uint objId, ushort tlId, ulong type, string creatorName, uint dbId) : base(SCOffsets.SCSlaveStatePacket, 1)
        {
            _objId = objId;
            _tlId = tlId;
            _type = type;
            _creatorName = creatorName;
            _dbId = dbId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.Write(_tlId);
            stream.Write(_type);
            stream.Write(_skillCount);
            stream.Write(_tagCount);
            stream.Write(_creatorName);
            stream.Write(_type2);
            stream.Write(_dbId);
            return stream;
        }
    }
}
