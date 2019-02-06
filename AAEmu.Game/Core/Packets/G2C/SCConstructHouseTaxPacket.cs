using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCConstructHouseTaxPacket : GamePacket
    {
        private readonly uint _designId;
        private readonly int _houseCount;
        private readonly int _moneyAmount;
        private readonly int _moneyAmount2;
        
        public SCConstructHouseTaxPacket(uint designId, int houseCount, int moneyAmount, int moneyAmount2) : base(0x0bd, 1)
        {
            _designId = designId;
            _houseCount = houseCount;
            _moneyAmount = moneyAmount;
            _moneyAmount2 = moneyAmount2;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_designId);
            stream.Write(_houseCount);
            stream.Write(_moneyAmount);
            stream.Write(_moneyAmount2);
            return stream;
        }
    }
}
