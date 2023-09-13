using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Internal;

namespace AAEmu.Login.Core.Packets.L2G
{
    public class LGPlayerEnterPacket : InternalPacket
    {
        private readonly uint _accountId;
        private readonly uint _connectionId;

        public LGPlayerEnterPacket(uint accountId, uint connectionId) : base(LGOffsets.LGPlayerEnterPacket)
        {
            _accountId = accountId;
            _connectionId = connectionId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_accountId);
            stream.Write(_connectionId);
            return stream;
        }
    }
}
