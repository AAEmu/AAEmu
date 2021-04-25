using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C
{
    public class TCJoinResponsePacket : StreamPacket
    {
        private readonly byte _response;

        public TCJoinResponsePacket(byte response) : base(TCOffsets.TCJoinResponsePacket)
        {
            _response = response;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_response);

            return stream;
        }
    }
}
