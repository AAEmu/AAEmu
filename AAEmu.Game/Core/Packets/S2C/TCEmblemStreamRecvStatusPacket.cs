using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C;

public class TCEmblemStreamRecvStatusPacket(EmblemStreamStatus status)
    : StreamPacket(TCOffsets.TCEmblemStreamRecvStatusPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)status); // status
        stream.Write(0);

        return stream;
    }
}

public enum EmblemStreamStatus
{
    Continue = 0,
    Start = 1,
    End = 2
}
