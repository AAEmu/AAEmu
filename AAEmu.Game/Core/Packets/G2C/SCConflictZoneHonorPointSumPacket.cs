using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCConflictZoneHonorPointSumPacket(ushort zoneId, int sum)
    : GamePacket(SCOffsets.SCConflictZoneHonorPointSumPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(zoneId);
        stream.Write(sum);
        return stream;
    }
}
