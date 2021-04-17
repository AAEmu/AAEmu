using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.L2G
{
    public class LGRegisterGameServerPacket : InternalPacket
    {
        private readonly GSRegisterResult _result;

        public LGRegisterGameServerPacket(GSRegisterResult result) : base(LGOffsets.LGRegisterGameServerPacket)
        {
            _result = result;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte) _result);
            return stream;
        }
    }
}
