using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C;

public enum StreamJoinResponse : sbyte
{
    Success = 0,
    Rejected = 1
}

public class TCJoinResponsePacket(StreamJoinResponse response) : StreamPacket(TCOffsets.TCJoinResponsePacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((sbyte)response);

        return stream;
    }
}
