using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C
{
    public class TCEmblemStreamRecvStatusPacket : StreamPacket
    {
        private EmblemStreamStatus _status;
        
        public TCEmblemStreamRecvStatusPacket(EmblemStreamStatus status) : base(TCOffsets.TCEmblemStreamRecvStatusPacket)
        {
            _status = status;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte) _status); // status
            stream.Write((int) 0);

            return stream;
        }
    }

    public enum EmblemStreamStatus
    {
        Continue = 0,
        Start = 1,
        End = 2
    }
}
