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
        private readonly int _moneyAmount;
        private readonly int _moneyAmount2;
        private readonly int _moneyAmount3; //Unused/Unsure what it does.
        private readonly int _moneyAmount4; //Unused/Unsure what it does.
        
        public SCConstructHouseTaxPacket(uint designId, int normalTaxHouseCount, int heavyTaxHouseCount, bool isHeavyTaxHouse, 
            int moneyAmount, int moneyAmount2, int moneyAmount3, int moneyAmount4) : base(SCOffsets.SCConstructHouseTaxPacket, 1)
        {
            _designId = designId;
            _normalTaxHouseCount = normalTaxHouseCount;
            _heavyTaxHouseCount = heavyTaxHouseCount;
            _isHeavyTaxHouse = isHeavyTaxHouse;
            _moneyAmount = moneyAmount;
            _moneyAmount2 = moneyAmount2;
            _moneyAmount3 = moneyAmount3;
            _moneyAmount4 = moneyAmount4;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_designId);
            stream.Write(_heavyTaxHouseCount);
            stream.Write(_normalTaxHouseCount);
            stream.Write(_isHeavyTaxHouse);
            stream.Write(_moneyAmount);
            stream.Write(_moneyAmount2);
            stream.Write(_moneyAmount3);
            stream.Write(_moneyAmount4);
            return stream;
        }
    }
}
