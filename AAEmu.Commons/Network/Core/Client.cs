using System.Collections.Generic;
using System.Net;

using NetCoreServer;

namespace AAEmu.Commons.Network.Core;

public class Client : TcpClient, ISession
{
    private readonly Dictionary<string, object> _attributes = [];
    private BaseProtocolHandler _handler;
    private uint _sessionId;
    private IPAddress _ip;

    IPAddress ISession.Ip => _ip;

    uint ISession.SessionId => _sessionId;

    void ISession.SendPacket(byte[] packet)
    {
        SendAsync(packet);
    }

    void ISession.AddAttribute(string name, object attribute)
    {
        _attributes.Add(name, attribute);
    }

    object ISession.GetAttribute(string name)
    {
        _attributes.TryGetValue(name, out var attribute);
        return attribute;
    }

    void ISession.ClearAttribute(string name)
    {
        _attributes.Remove(name);
    }

    void ISession.Close()
    {
        Disconnect();
    }

    public Client(IPAddress serverAddress, int serverPort, BaseProtocolHandler handler) : base(serverAddress, serverPort)
    {
        _handler = handler;
    }

    public BaseProtocolHandler GetHandler()
    {
        return _handler;
    }

    protected override void OnConnected()
    {
        _sessionId = (uint)Socket.LocalEndPoint.GetHashCode();
        _ip = ((IPEndPoint)Socket.LocalEndPoint).Address;
        _handler.OnConnect(this);
    }

    protected override void OnDisconnected()
    {
        _handler.OnDisconnect(this);
    }

    protected override void OnReceived(byte[] buffer, long offset, long size)
    {
        _handler.OnReceive(this, buffer, (int)offset, (int)size);
    }

    protected override void OnSent(long sent, long pending)
    {
        base.OnSent(sent, pending);
    }
}
