using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

// SC_PACKET_TAX_ITEM_CONFIG (676). Body:
// single u64 `convertRatioToAAPoint`.
public class SCTaxItemConfigPacket(ulong convertRatioToAAPoint) : GamePacket(SCOffsets.SCTaxItemConfigPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(convertRatioToAAPoint);
        return stream;
    }
}
