using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTradeOkUpdatePacket : GamePacket
    {
        private readonly bool _myOk;
        private readonly bool _otherOk;

        public SCTradeOkUpdatePacket(bool myOk, bool otherOk) : base(SCOffsets.SCTradeOkUpdatePacket, 5)
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
