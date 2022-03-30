using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCOtherTradeOkPacket : GamePacket
    {
        private readonly bool _myOk;
        private readonly bool _otherOk;

        public SCOtherTradeOkPacket(bool myOk, bool otherOk) : base(SCOffsets.SCOtherTradeOkPacket, 5)
        {
            _myOk = myOk;
            _otherOk = otherOk;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_myOk);
            stream.Write(_otherOk);

            return stream;
        }
    }
}
