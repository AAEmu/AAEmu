using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCICSBuyResultPacket : GamePacket
    {
        private readonly bool _success;
        private readonly byte _buyMode;
        private readonly string _receiverName;
        private readonly int _chargeAaPoint;
        
        public SCICSBuyResultPacket(bool success, byte buyMode, string receiverName, int chargeAaPoint) : base(SCOffsets.SCICSBuyResultPacket, 5)
        {
            _success = success;
            _buyMode = buyMode;
            _receiverName = receiverName;
            _chargeAaPoint = chargeAaPoint;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_success);
            stream.Write(_buyMode);
            stream.Write(_receiverName);
            stream.Write(_chargeAaPoint);
            return stream;
        }
    }
}
