using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCompletedQuestsPacket(CompletedQuest[] quests) : GamePacket(SCOffsets.SCCompletedQuestsPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(quests.Length);
        foreach (var quest in quests)
        {
            var body = new byte[8];
            quest.Body.CopyTo(body, 0);

            stream.Write((uint)quest.Id); // idx — client reads u32 (ushort Write desyncs / crash)
            stream.Write(body); // body
        }
        return stream;
    }
}
