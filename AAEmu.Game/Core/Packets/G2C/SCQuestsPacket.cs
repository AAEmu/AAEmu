using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCQuestsPacket(Quest[] quests) : GamePacket(SCOffsets.SCQuestsPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(quests.Length); // count // TODO max 20
        foreach (var quest in quests)
            stream.Write(quest);
        return stream;
    }
}
