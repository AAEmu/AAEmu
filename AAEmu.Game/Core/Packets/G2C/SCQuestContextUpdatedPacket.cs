using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCQuestContextUpdatedPacket(
    Quest quest,
    uint componentId,
    int para1 = 0,
    int para2 = 0,
    int para3 = 0,
    int para4 = 0)
    : GamePacket(SCOffsets.SCQuestContextUpdatedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(quest);
        stream.WritePisc(
            componentId,
            (uint)Math.Max(0, para1),
            (uint)Math.Max(0, para2),
            (uint)Math.Max(0, para3),
            (uint)Math.Max(0, para4),
            0u, 0u, 0u, 0u, 0u);
        return stream;
    }
}
