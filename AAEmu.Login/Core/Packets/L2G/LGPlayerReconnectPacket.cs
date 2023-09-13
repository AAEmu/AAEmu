using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Internal;

namespace AAEmu.Login.Core.Packets.L2G
{
    public class LGPlayerReconnectPacket : InternalPacket
    {
        private readonly uint _token;

        public LGPlayerReconnectPacket(uint token) : base(LGOffsets.LGPlayerReconnectPacket)
        {
            _token = token;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_token);
            return stream;
        }
    }
}
