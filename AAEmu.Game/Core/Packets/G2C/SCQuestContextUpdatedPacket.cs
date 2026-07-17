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
    : GamePacket(SCOffsets.SCQuestContextUpdatedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(quest);
        stream.Write(componentId); // componentId
        stream.Write(para1); // type
        stream.Write(para2); // type
        stream.Write(para3); // type
        stream.Write(para4); // type
        // Needs 4 int parameters at minimum, but adding more doesn't seem to break the packet
        // Changing the values of these doesn't seem to have any visible effect.
        return stream;
    }
}
