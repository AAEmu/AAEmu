using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Models.Stream;

namespace AAEmu.Game.Core.Packets.S2C;

public class TCEmblemStreamSendStatusPacket(Ucc ucc, EmblemStreamStatus status)
    : StreamPacket(TCOffsets.TCEmblemStreamSendStatusPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((long)ucc.Id); // type
        if (ucc is CustomUcc customUcc)
            stream.Write(customUcc.Data.Count); // total data bytes 
        else
            stream.Write(0); // total
        ucc.Write(stream);

        stream.Write((byte)status); // status

        return stream;
    }
}
