using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C
{
    public class TCUccComplexCheckValidPacket : StreamPacket
    {
        private readonly long _type;
        private readonly bool _isValid;

        public TCUccComplexCheckValidPacket(long type, bool isValid) : base(0x0F)
        {
            _type = type;
            _isValid = isValid;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_type);
            stream.Write(_isValid);

            return stream;
        }
    }
}