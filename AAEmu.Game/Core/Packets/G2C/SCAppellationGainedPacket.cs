using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAppellationGainedPacket(uint appellationId) : GamePacket(SCOffsets.SCAppellationGainedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(appellationId);
        return stream;
    }
}
