using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCHouseTaxInfoPacket : GamePacket
    {
        private readonly ushort _tl;
        private readonly int _dominionTaxRate;
        private readonly int _moneyAmount;
        private readonly ulong _due;
        private readonly int _weeksWithoutPay;
        private readonly bool _isAlreadyPaid;
        
        public SCHouseTaxInfoPacket(ushort tl, int dominionTaxRate, int moneyAmount, ulong due, int weeksWithoutPay, bool isAlreadyPaid)
            : base(0x0bc, 1)
        {
            _tl = tl;
            _dominionTaxRate = dominionTaxRate;
            _moneyAmount = moneyAmount;
            _due = due;
            _weeksWithoutPay = weeksWithoutPay;
            _isAlreadyPaid = isAlreadyPaid;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_tl);
            stream.Write(_dominionTaxRate);
            stream.Write(_moneyAmount);
            stream.Write(_due);
            stream.Write(_weeksWithoutPay);
            stream.Write(_isAlreadyPaid);
            return stream;
        }
    }
}
