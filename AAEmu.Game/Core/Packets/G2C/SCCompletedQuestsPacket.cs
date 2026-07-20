using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCompletedQuestsPacket(CompletedQuest[] quests) : GamePacket(SCOffsets.SCCompletedQuestsPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(quests.Length); // TODO max 200
        foreach (var quest in quests)
        {
            var body = new byte[8];
            quest.Body.CopyTo(body, 0);

            stream.Write(quest.Id); // idx
            stream.Write(body); // body
        }
        return stream;
    }
}
