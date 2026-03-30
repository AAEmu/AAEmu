using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCRulingClosedPacket() : GamePacket(SCOffsets.SCRulingClosedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // Empty
        return stream;
    }
}
