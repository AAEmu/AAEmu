using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCInGameShopConfigPacket : GamePacket
    {
        private readonly byte _ingameShopVersion;
        private readonly byte _secondPriceType;
        private readonly byte _askBuyLaborPowerPotion;

        public SCInGameShopConfigPacket(byte ingameShopVersion, byte secondPriceType, byte askBuyLaborPowerPotion)
            : base(SCOffsets.SCInGameShopConfigPacket, 5)
        {
            _ingameShopVersion = ingameShopVersion;
            _secondPriceType = secondPriceType;
            _askBuyLaborPowerPotion = askBuyLaborPowerPotion;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_ingameShopVersion);
            stream.Write(_secondPriceType);
            stream.Write(_askBuyLaborPowerPotion);

            return stream;
        }
    }
}
