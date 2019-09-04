using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Internal;

namespace AAEmu.Login.Core.Packets.L2G
{
    public class LGRequestInfoPacket : InternalPacket
    {
        private readonly uint _connectionId;
        private readonly uint _requestId;
        private readonly ulong _accountId;

        public LGRequestInfoPacket(uint connectionId, uint requestId, uint accountId) : base(0x03)
        {
            _connectionId = connectionId;
            _requestId = requestId;
            _accountId = accountId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_connectionId);
            stream.Write(_requestId);
            stream.Write(_accountId);
            return stream;
        }
    }
}
