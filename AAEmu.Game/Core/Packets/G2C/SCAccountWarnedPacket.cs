using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAccountWarnedPacket : GamePacket
    {
        private readonly byte _source;
        private readonly string _msg;

        public SCAccountWarnedPacket(byte source, string msg) : base(SCOffsets.SCAccountWarnedPacket, 5)
        {
            _source = source;
            _msg = msg;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_source);
            stream.Write(_msg);
            return stream;
        }
    }
}
