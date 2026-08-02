using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class SCResetMerchantGoodLimitPurchasePacket(sbyte resetPurchaseType) : GamePacket(SCOffsets.SCResetMerchantGoodLimitPurchasePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(resetPurchaseType);
        return stream;
    }
}
