using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTradeCanceledPacket : GamePacket
    {
        private readonly int _reason;
        private readonly bool _causedByMe;

        public SCTradeCanceledPacket(int reason, bool causedByMe) : base(SCOffsets.SCTradeCanceledPacket, 5)
        {
            _reason = reason;
            _causedByMe = causedByMe;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_reason);
            stream.Write(_causedByMe);
            return stream;
        }
    }
}
