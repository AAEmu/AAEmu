using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Merchant;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// <c>purchaseType</c>, i32 <c>purchaseLimit</c>.
/// </summary>
public class SCBuyFailedMerchantGoodLimitPurchasePacket(
    uint itemTemplateId,
    MerchantPurchaseType purchaseType,
    int purchaseLimit) : GamePacket(SCOffsets.SCBuyFailedMerchantGoodLimitPurchasePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(itemTemplateId);
        stream.Write((byte)purchaseType);
        stream.Write(purchaseLimit);
        return stream;
    }
}
