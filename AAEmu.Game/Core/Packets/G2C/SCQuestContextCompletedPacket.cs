using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCQuestContextCompletedPacket(uint questId, byte[] body, uint componentId)
    : GamePacket(SCOffsets.SCQuestContextCompletedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(questId);
        stream.Write(body); // ulong -> byte[8]
        stream.Write(componentId);
        return stream;
    }
}
