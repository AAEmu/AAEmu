using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

// SC_PACKET_HOUSING_AREA_CONFIG (701). Body per x2game-dev_dedicate sub_39C4C200 → sub_39C4AB50:
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
