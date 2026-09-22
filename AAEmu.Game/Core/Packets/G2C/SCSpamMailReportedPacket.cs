using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Acknowledges a spam report. The client reads no payload from this packet.</summary>
public class SCSpamMailReportedPacket() : GamePacket(SCOffsets.SCSpamMailReportedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        return stream;
    }
}
