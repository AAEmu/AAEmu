using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCHouseSetForSalePacket : GamePacket
    {
        private readonly ushort _tl;
        private readonly uint _moneyAmount;
        private readonly uint _sellToPlayerId;
        private readonly string _sellToName;
        private readonly string _houseName;
        
        public SCHouseSetForSalePacket(ushort tl, uint moneyAmount, uint sellToPlayerId, string sellToName, string houseName) : base(SCOffsets.SCHouseSetForSalePacket, 1)
        {
            _tl = tl;
            _moneyAmount = moneyAmount;
            _sellToPlayerId = sellToPlayerId;
            _sellToName = sellToName;
            _houseName = houseName;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_tl);
            stream.Write(_moneyAmount);
            stream.Write(_sellToPlayerId);
            stream.Write(_sellToName);
            stream.Write(_houseName);
            return stream;
        }
    }
}
