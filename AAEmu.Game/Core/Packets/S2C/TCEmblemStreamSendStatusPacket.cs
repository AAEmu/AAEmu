using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Models.Stream;

namespace AAEmu.Game.Core.Packets.S2C
{
    public class TCEmblemStreamSendStatusPacket : StreamPacket
    {
        private Ucc _ucc;
        private EmblemStreamStatus _emblemStreamStatus;
        public TCEmblemStreamSendStatusPacket(Ucc ucc, EmblemStreamStatus status) : base(TCOffsets.TCEmblemStreamSendStatusPacket)
        {
            _ucc = ucc;
            _emblemStreamStatus = status;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((long) _ucc.Id); // type
            if (_ucc is CustomUcc customUcc)
                stream.Write((int)customUcc.Data.Count); // total data bytes 
            else
                stream.Write((int)0); // total
            _ucc.Write(stream);
            
            stream.Write((byte) _emblemStreamStatus); // status

            return stream;
        }
    }
}
