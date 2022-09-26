using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCConstructHouseTaxPacket : GamePacket
    {
        private readonly uint _designId;
        private readonly int _heavyTaxHouseCount;
        private readonly int _normalTaxHouseCount;
        private readonly bool _isHeavyTaxHouse;
        private readonly int _baseTaxMoneyAmount;
        private readonly int _depositTaxMoneyAmount;
        private readonly int _totalTaxMoneyAmount;

        public SCConstructHouseTaxPacket(uint designId, int heavyTaxHouseCount, int normalTaxHouseCount,
            bool isHeavyTaxHouse, int baseTaxMoneyAmount, int depositTaxMoneyAmount, int totalTaxMoneyAmount)
            : base(SCOffsets.SCConstructHouseTaxPacket, 5)
        {
            _designId = designId;
            _heavyTaxHouseCount = heavyTaxHouseCount;
            _normalTaxHouseCount = normalTaxHouseCount;
            _isHeavyTaxHouse = isHeavyTaxHouse;
            _baseTaxMoneyAmount = baseTaxMoneyAmount;
            _depositTaxMoneyAmount = depositTaxMoneyAmount;
            _totalTaxMoneyAmount = totalTaxMoneyAmount;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_designId);
            stream.Write(_heavyTaxHouseCount);
            stream.Write(_normalTaxHouseCount);
            stream.Write(_isHeavyTaxHouse);
            stream.Write(_baseTaxMoneyAmount);
            stream.Write(_depositTaxMoneyAmount);
            stream.Write(_totalTaxMoneyAmount);
            return stream;
        }
    }
}
