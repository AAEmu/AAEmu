using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCRankAlarmPacket(uint type, byte rankAlarmKind) : GamePacket(SCOffsets.SCRankAlarmPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(type);
        stream.Write(rankAlarmKind);
        return stream;
    }
}
