using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.L2C
{
    public class ACWorldCookiePacket : LoginPacket
    {
        private readonly LoginConnection _connection;
        private readonly uint _cookie;
        private readonly GameServer _gs;

        public ACWorldCookiePacket(LoginConnection connection, GameServer gs) : base(LCOffsets.ACWorldCookiePacket)
        {
            _connection = connection;
            _cookie = connection.Id;
            _gs = gs;
        }

        public override PacketStream Write(PacketStream stream)
        {
            var serverIp = _gs.Host;
            if (_connection.IsLocallyConnected)
                serverIp = _connection.Ip.ToString();
            stream.Write(_cookie);
            for (var i = 0; i < 4; i++)
            {
                stream.Write(Helpers.ConvertIp(serverIp));
                stream.Write(_gs.Port);
            }

            return stream;
        }
    }
}
