using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Merchant;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// </summary>
public class SCUpdateMerchantGoodLimitPurchasePacket(
    IReadOnlyDictionary<uint, MerchantPurchaseState> states) :
    GamePacket(SCOffsets.SCUpdateMerchantGoodLimitPurchasePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((uint)states.Count);
        foreach (var (itemTemplateId, state) in states.OrderBy(entry => entry.Key))
        {
            stream.Write(itemTemplateId);
            stream.Write(state.BuyCount);
            stream.Write((byte)state.PurchaseType);
        }
        return stream;
    }
}
