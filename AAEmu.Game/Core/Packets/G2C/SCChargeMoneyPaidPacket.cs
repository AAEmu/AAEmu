using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCChargeMoneyPaidPacket : GamePacket
    {
        private readonly long _mailId;
        
        public SCChargeMoneyPaidPacket(long mailId) : base(SCOffsets.SCChargeMoneyPaidPacket, 5)
        {
            _mailId = mailId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_mailId);
            return stream;
        }
    }
}
