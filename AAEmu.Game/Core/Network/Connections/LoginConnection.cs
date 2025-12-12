using System.Net;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Login;
using AAEmu.Game.Core.Packets.G2L;
using AAEmu.Game.Models;

namespace AAEmu.Game.Core.Network.Connections;

public class LoginConnection(ISession session)
{
    public uint Id => session.SessionId;
    public IPAddress Ip => session.Ip;

    public bool Block { get; set; }
    public PacketStream LastPacket { get; set; }

    public void OnConnect()
    {
        var secretKey = AppConfiguration.Instance.SecretKey;
        var gsId = AppConfiguration.Instance.Id;
        var additionalesGsId = AppConfiguration.Instance.AdditionalesId;
        SendPacket(new GLRegisterGameServerPacket(secretKey, gsId, additionalesGsId));
    }

    public void SendPacket(LoginPacket packet)
    {
        if (Block)
            return;
        packet.Connection = this;
        byte[] buf = packet.Encode();
        session.SendPacket(buf);
    }

    public void Close()
    {
        session.Close();
    }
}
