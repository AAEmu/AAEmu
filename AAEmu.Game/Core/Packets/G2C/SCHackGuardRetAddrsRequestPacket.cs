using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCHackGuardRetAddrsRequestPacket : GamePacket
    {
        private readonly bool _sendAddrs;
        private readonly bool _spMd5;

        public SCHackGuardRetAddrsRequestPacket(bool sendAddrs, bool spMd5) : base(SCOffsets.SCHackGuardRetAddrsRequestPacket, 1)
        {
            _sendAddrs = sendAddrs;
            _spMd5 = spMd5;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_sendAddrs);
            stream.Write(_spMd5);
            return stream;
        }
    }
}
