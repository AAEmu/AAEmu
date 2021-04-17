using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class FastPongPacket : GamePacket
    {
        private readonly uint _sent;

        public FastPongPacket(uint sent) : base(PPOffsets.FastPongPacket, 2)
        {
            _sent = sent;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_sent);
            return stream;
        }
    }
}
