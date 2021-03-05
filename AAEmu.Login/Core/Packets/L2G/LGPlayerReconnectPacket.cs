using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Internal;

namespace AAEmu.Login.Core.Packets.L2G
{
    public class LGPlayerReconnectPacket : InternalPacket
    {
        private readonly ulong _token;

        public LGPlayerReconnectPacket(ulong accountId) : base(0x02)
        {
            _token = accountId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_token);
            return stream;
        }
    }
}
