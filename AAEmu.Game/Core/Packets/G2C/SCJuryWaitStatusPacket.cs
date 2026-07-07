using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCJuryWaitStatusPacket(int count, int total, int sentenceTimeInMs)
    : GamePacket(SCOffsets.SCJuryWaitStatusPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(count);
        stream.Write(total);
        stream.Write(sentenceTimeInMs);
        return stream;
    }
}
