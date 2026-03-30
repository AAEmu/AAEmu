using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTrialWaitStatusPacket(uint order, int sentenceTimeInMs) : GamePacket(SCOffsets.SCTrialWaitStatusPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(order);
        stream.Write(sentenceTimeInMs);
        return stream;
    }
}
