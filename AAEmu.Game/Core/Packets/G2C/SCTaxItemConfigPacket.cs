using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

// SC_PACKET_TAX_ITEM_CONFIG (676). Body per x2game-dev_dedicate sub_3953FFA0:
// single u64 `convertRatioToAAPoint` (vtbl+120).
public class SCTaxItemConfigPacket(ulong convertRatioToAAPoint) : GamePacket(SCOffsets.SCTaxItemConfigPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(convertRatioToAAPoint);
        return stream;
    }
}
