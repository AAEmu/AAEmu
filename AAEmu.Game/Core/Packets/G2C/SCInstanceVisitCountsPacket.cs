using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCInstanceVisitCountsPacket() : GamePacket(SCOffsets.SCInstanceVisitCountsPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // Count of per-instance visit records; the reference sends 0 at world entry.
        stream.Write(0u);

        return stream;
    }
}
