using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitPvPPointsChangedPacket(uint unitId, byte kind, int point)
    : GamePacket(SCOffsets.SCUnitPvPPointsChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(kind);
        stream.Write(point);
        return stream;
    }
}
