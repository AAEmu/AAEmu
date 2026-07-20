using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCQuestContextStartedPacket(Quest quest, uint componentId)
    : GamePacket(SCOffsets.SCQuestContextStartedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(quest);
        stream.Write(componentId);
        return stream;
    }
}
