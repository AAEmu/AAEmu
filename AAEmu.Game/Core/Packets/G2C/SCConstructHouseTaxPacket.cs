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
        private readonly int _moneyAmount;
        private readonly int _hostileTaxRate;

        public SCConstructHouseTaxPacket(uint designId, int heavyTaxHouseCount, int normalTaxHouseCount,
            bool isHeavyTaxHouse, int baseTaxMoneyAmount, int depositTaxMoneyAmount, int totalTaxMoneyAmount, int moneyAmount, int hostileTaxRate)
            : base(SCOffsets.SCConstructHouseTaxPacket, 5)
        {
            _designId = designId;
            _heavyTaxHouseCount = heavyTaxHouseCount;
            _normalTaxHouseCount = normalTaxHouseCount;
            _isHeavyTaxHouse = isHeavyTaxHouse;
            _baseTaxMoneyAmount = baseTaxMoneyAmount;
            _depositTaxMoneyAmount = depositTaxMoneyAmount;
            _totalTaxMoneyAmount = totalTaxMoneyAmount;
            _moneyAmount = moneyAmount;
            _hostileTaxRate = hostileTaxRate;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_designId);             // design
            stream.Write(_heavyTaxHouseCount);   // heavyTaxHouseCount
            stream.Write(_normalTaxHouseCount);  // normalTaxHouseCount
            stream.Write(_isHeavyTaxHouse);      // isHeavyTaxHouse
            stream.Write(_baseTaxMoneyAmount);   // moneyAmount
            stream.Write(_depositTaxMoneyAmount);// moneyAmount
            stream.Write(_totalTaxMoneyAmount);  // moneyAmount
            stream.Write(_moneyAmount);          // moneyAmount
            stream.Write(_hostileTaxRate);       // hostileTaxRate
            return stream;
        }
    }
}
