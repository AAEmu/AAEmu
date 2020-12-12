using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Managers
{
    internal class SCChargeMoneyPaid : GamePacket
    {
        private long _mailid;

        public SCChargeMoneyPaid(long id) : base(SCOffsets.SCChargeMoneyPaidPacket, 1)
        {
            _mailid = id;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_mailid);
            return stream;
        }
    }
}
