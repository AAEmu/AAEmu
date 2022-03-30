using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitBountyMoneyPacket : GamePacket
    {
        private readonly uint _objId;
        private readonly long _moneyAmount;

        public SCUnitBountyMoneyPacket(uint objId, long moneyAmount) : base(SCOffsets.SCUnitBountyMoneyPacket, 5)
        {
            _objId = objId;
            _moneyAmount = moneyAmount;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.Write(_moneyAmount);
            return stream;
        }
    }
}
