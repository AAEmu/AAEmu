using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Crime;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCRulingStatusPacket(int count, int total, TrialSentenceResult sentenceType, int sentenceTime)
    : GamePacket(SCOffsets.SCRulingStatusPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(count);
        stream.Write(total);
        stream.Write((byte)sentenceType);
        stream.Write(sentenceTime);
        return stream;
    }
}
