using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C
{
    public class TCUccComplexCheckValidPacket : StreamPacket
    {
        private readonly ulong _type;
        private readonly bool _isValid;

        public TCUccComplexCheckValidPacket(ulong type, bool isValid) : base(TCOffsets.TCUccComplexCheckValidPacket)
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
