using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCChargeMoneyPaidPacket : GamePacket
    {
        private readonly long _mailId;
        private readonly int _moneyAmount1;
        private readonly int _moneyAmount2;
        
        public SCChargeMoneyPaidPacket(long mailId, int moneyAmount1, int moneyAmount2) : base(0x118, 1)
        {
            _mailId = mailId;
            _moneyAmount1 = moneyAmount1;
            _moneyAmount2 = moneyAmount2;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_mailId);
            stream.Write(_moneyAmount1);
            stream.Write(_moneyAmount2);
            return stream;
        }
    }
}
