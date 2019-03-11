using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCKickedPacket : GamePacket
    {
        private readonly byte _reason;
        private readonly string _msg;

        public SCKickedPacket(byte reason, string msg) : base(SCOffsets.SCKickedPacket, 1)
        {
            _reason = reason;
            _msg = msg;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_reason);
            stream.Write(_msg);
            return stream;
        }
    }
}
