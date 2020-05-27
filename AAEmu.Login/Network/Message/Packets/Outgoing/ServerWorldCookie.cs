using System.Linq;
using System.Net;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Network.Core.Messages;
using AAEmu.Login.Login;

namespace AAEmu.Login.Network.Message.Packets.Outgoing
{
    [LoginMessage(LoginMessageOpcode.ServerWorldCookie)]
    public class ServerWorldCookie : IWritable
    {
        public uint ConnectionId { get; set; }
        public GameServerInstance GameServer { get; set; }
        
        public void Write(PacketStream stream)
        {
            stream.Write(ConnectionId);
            for (var i = 0; i < 4; i++)
            {
                stream.Write(ConvertIp(GameServer.GameServer.Host));
                stream.Write((ushort)GameServer.GameServer.Port);
            }
        }
        
        private byte[] ConvertIp(string ip)
        {
            var result = IPAddress.Parse(ip);
            return result.GetAddressBytes().Reverse().ToArray();
        }
    }
}
