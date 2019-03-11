using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.L2C
{
    public class ACWorldCookiePacket : LoginPacket
    {
        private readonly int _cookie;
        private readonly GameServer _gs;

        public ACWorldCookiePacket(int cookie, GameServer gs) : base(0x0A)
        {
            _cookie = cookie;
            _gs = gs;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_cookie);
            for (var i = 0; i < 4; i++)
            {
                stream.Write(Helpers.ConvertIp(_gs.Host));
                stream.Write(_gs.Port);
            }

            return stream;
        }
    }
}
