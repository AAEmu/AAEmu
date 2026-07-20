using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCEmotionExpressedPacket(uint objId, uint objId2, uint emotionId)
    : GamePacket(SCOffsets.SCEmotionExpressedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.WriteBc(objId2);
        stream.Write(emotionId);
        return stream;
    }
}
