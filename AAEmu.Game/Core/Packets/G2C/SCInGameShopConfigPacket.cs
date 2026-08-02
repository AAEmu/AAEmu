using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

// ingameShopVersion u8, secondPriceType u8, askBuyLaborPowerPotion bool.
public class SCInGameShopConfigPacket(byte ingameShopVersion, byte secondPriceType, bool askBuyLaborPowerPotion)
    : GamePacket(SCOffsets.SCInGameShopConfigPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(ingameShopVersion);
        stream.Write(secondPriceType);
        stream.Write(askBuyLaborPowerPotion);
        return stream;
    }
}
