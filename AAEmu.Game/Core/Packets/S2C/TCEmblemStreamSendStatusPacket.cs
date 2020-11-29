using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Models.Stream;

namespace AAEmu.Game.Core.Packets.S2C
{
    public class TCEmblemStreamSendStatusPacket : StreamPacket
    {
        private Ucc _ucc;
        private EmblemStreamStatus _emblemStreamStatus;
        public TCEmblemStreamSendStatusPacket(Ucc ucc, EmblemStreamStatus status) : base(0x0B)
        {
            _ucc = ucc;
            _emblemStreamStatus = status;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((long) _ucc.Id); // type
            stream.Write((int) 0); // total
            _ucc.Write(stream);
            
            stream.Write((byte) _emblemStreamStatus); // status

            return stream;
        }
    }
}
