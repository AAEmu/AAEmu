using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

// SC_PACKET_WORLD_LEVEL_INFO (906). Carries the world-level state the client's player-frame gauge reads to render
// the world-level-vs-character-level indicator. Without it the client's world-level data pointer stays null and
// the gauge provider dereferences it on show. Values mirror the reference world-entry capture.
public class SCWorldLevelInfoPacket() : GamePacket(SCOffsets.SCWorldLevelInfoPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(42u);          // worldLevel
        stream.Write(6u);           // grade
        stream.Write(10000u);       // exp
        stream.Write(0u);           // nextExp
        stream.Write(1u);           // enabled
        stream.Write(unchecked((uint)-41)); // bonus
        return stream;
    }
}
