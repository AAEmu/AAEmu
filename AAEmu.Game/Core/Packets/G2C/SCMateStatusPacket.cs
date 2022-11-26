using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMateStatusPacket : GamePacket
    {
        private readonly uint _objId;
        private readonly uint _skillCount;
        private readonly uint _tagCount;

        public SCMateStatusPacket(uint objId) : base(SCOffsets.SCMateStatusPacket, 5)
        {
            _objId = objId;
            _skillCount = 0;
            _tagCount = 0;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.Write(_skillCount); // skillCount
            for (var i = 0; i < _skillCount; i++)
            {
                stream.Write(0u); // type
                stream.Write(0u); // type
                stream.Write(0u); // type
            }

            stream.Write(_tagCount); // tagCount
            for (var i = 0; i < _tagCount; i++)
            {
                stream.Write(0u); // type
                stream.Write(0u); // type
                stream.Write(0u); // type
            }
            
            return stream;
        }
    }
}
