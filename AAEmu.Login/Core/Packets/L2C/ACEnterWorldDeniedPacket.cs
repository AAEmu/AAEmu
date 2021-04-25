using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C
{
    public class ACEnterWorldDeniedPacket : LoginPacket
    {
        private readonly byte _reason;
        
        public ACEnterWorldDeniedPacket(byte reason) : base(LCOffsets.ACEnterWorldDeniedPacket)
        {
            _reason = reason;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_reason);

            return stream;
        }
    }
}
