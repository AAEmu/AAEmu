using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCQuestRewardedByMailPacket(uint[] questList) : GamePacket(SCOffsets.SCQuestRewardedByMailPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)questList.Length);
        foreach (var questId in questList)
            stream.Write(questId);
        return stream;
    }
}
