using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

// a map serialized as `Size` u32 followed by Size × { key, value }. Sent empty during context
// establishment (Size = 0).
public class SCHousingAreaConfigPacket() : GamePacket(SCOffsets.SCHousingAreaConfigPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(0u); // Size
        return stream;
    }
}
