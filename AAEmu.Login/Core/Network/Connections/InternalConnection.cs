using System.Net;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Network.Connections;

public class InternalConnection(ISession session)
{
    public uint Id => session.SessionId;
    public IPAddress Ip => session.Ip;
    public GameServer? GameServer { get; set; }
    public bool Block { get; set; }
    public PacketStream? LastPacket { get; set; }

    public static void OnConnect()
    {
    }

    public void SendPacket(InternalPacket packet)
    {
        if (Block)
            return;
        packet.Connection = this;
        byte[] buf = packet.Encode();
        session.SendPacket(buf);
    }

    public void AddAttribute(string name, object value)
    {
        session.AddAttribute(name, value);
    }
}
