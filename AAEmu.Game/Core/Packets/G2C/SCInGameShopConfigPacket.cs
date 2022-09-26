using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCInGameShopConfigPacket : GamePacket
    {
        private readonly byte _ingameShopVersio;
        private readonly byte _secondPriceType;
        private readonly byte _askBuyLaborPowerPotion;

        public SCInGameShopConfigPacket(byte ingameShopVersio, byte secondPriceType, byte askBuyLaborPowerPotion) : base(SCOffsets.SCInGameShopConfigPacket, 5)
        {
            _ingameShopVersio = ingameShopVersio;
            _secondPriceType = secondPriceType;
            _askBuyLaborPowerPotion = askBuyLaborPowerPotion;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_ingameShopVersio);
            stream.Write(_secondPriceType);
            stream.Write(_askBuyLaborPowerPotion);

            return stream;
        }
    }
}
