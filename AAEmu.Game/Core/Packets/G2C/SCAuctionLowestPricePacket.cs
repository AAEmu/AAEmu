using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    class SCAuctionLowestPricePacket : GamePacket
    {
        private readonly uint _itemTemplateId;
        private readonly byte _itemGrade;
        private readonly int _moneyAmount;

        public SCAuctionLowestPricePacket(uint itemTemplateId, byte itemGrade) : base(SCOffsets.SCAuctionLowestPricePacket, 5)
        {
            _itemTemplateId = itemTemplateId;
            _itemGrade = itemGrade;
            _moneyAmount = 0;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_itemTemplateId); // itemTemplateId
            stream.Write(_itemGrade); // itemGrade
            stream.Write(_moneyAmount);

            return stream;
        }
    }
}
