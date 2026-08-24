using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Indun;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCInstanceVisitCountChangePacket(InstanceVisitCountRecord record)
    : GamePacket(SCOffsets.SCInstanceVisitCountChangePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        SCInstanceVisitCountsPacket.WriteVisitRow(stream, record);
        return stream;
    }
}
