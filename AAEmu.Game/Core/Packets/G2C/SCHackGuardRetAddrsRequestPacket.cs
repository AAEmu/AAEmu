using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCHackGuardRetAddrsRequestPacket : GamePacket
    {
        private readonly bool _sendAddrs;
        private readonly bool _spMd5;
        private readonly bool _spLuaMd5;
        private readonly string _dir;
        private readonly bool _modPack;

        public SCHackGuardRetAddrsRequestPacket(bool sendAddrs, bool spMd5) : base(SCOffsets.SCHackGuardRetAddrsRequestPacket, 5)
        {
            _sendAddrs = sendAddrs;
            _spMd5 = spMd5;
            _spLuaMd5 = true;
            _dir = "x2ui/hud";
            _modPack = false;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_sendAddrs);
            stream.Write(_spMd5);
            stream.Write(_spLuaMd5);
            stream.Write(_dir);
            stream.Write(_modPack);
            return stream;
        }
    }
}
