using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C;

public class TCJoinResponsePacket(byte response) : StreamPacket(TCOffsets.TCJoinResponsePacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(response);

        return stream;
    }
}
