using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.L2C;

public class ACWorldCookiePacket(LoginConnection connection, GameServer gs) : LoginPacket(LCOffsets.ACWorldCookiePacket)
{
    private readonly uint _cookie = connection.Id.Value;

    public override PacketStream Write(PacketStream stream)
    {
        var serverIp = gs.Host;
        if (connection.IsLocallyConnected)
            serverIp = connection.Ip.ToString();
        stream.Write(_cookie);
        for (var i = 0; i < 4; i++)
        {
            stream.Write(Helpers.ConvertIp(serverIp));
            stream.Write(gs.Port);
        }

        return stream;
    }
}
