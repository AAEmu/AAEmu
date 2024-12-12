using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using NetCoreServer;

namespace AAEmu.Commons.Network.Core;

public interface ISession
{
    IPAddress Ip { get; }
    uint SessionId { get; }
    Socket Socket { get; }
    void SendPacket(byte[] packet);
    void AddAttribute(string name, object attribute);
    object GetAttribute(string name);
    void ClearAttribute(string name);
    void Close();
}

public class Session : TcpSession, ISession
{
    private readonly Dictionary<string, object> _attributes = [];

    public BaseProtocolHandler ProtocolHandler { get; private set; }
    public IPEndPoint RemoteEndPoint { get; private set; }
    public uint SessionId { get; private set; }
    public IPAddress Ip { get; private set; }

    public Session(Server server) : base(server)
    {
        ProtocolHandler = server.GetHandler();
    }

    protected override void OnConnecting()
    {
        RemoteEndPoint = (IPEndPoint)Socket.RemoteEndPoint;
        SessionId = (uint)RemoteEndPoint.GetHashCode();
        Ip = RemoteEndPoint.Address;
        ProtocolHandler?.OnConnect(this);
    }

    protected override void OnConnected()
    {
        // Moved to OnConnecting due to a bug in TcpSession where OnReceived can happen before OnConnected.
        //_remoteEndPoint = (IPEndPoint)Socket.RemoteEndPoint;
        //_sessionId = (uint)RemoteEndPoint.GetHashCode();
        //_ip = RemoteEndPoint.Address;
        //ProtocolHandler?.OnConnect(this);
    }

    protected override void OnDisconnected()
    {
        ProtocolHandler?.OnDisconnect(this);
    }

    protected override void OnReceived(byte[] buffer, long offset, long size)
    {
        ProtocolHandler?.OnReceive(this, buffer, (int)offset, (int)size);
    }

    protected override void OnSent(long sent, long pending)
    {
    }

    protected override void OnError(SocketError error)
    {
    }

    public virtual void SendMessage(PacketStream message)
    {
        // var stream = new PacketStream();
        // message.Write(stream);
        SendAsync(message);
    }

    public override bool SendAsync(byte[] buffer)
    {
        // TODO send to queue
        return SendAsync(buffer, 0L, buffer.Length);
    }

    public void AddAttribute(string name, object attribute)
    {
        _attributes.Add(name, attribute);
    }

    public object GetAttribute(string name)
    {
        _attributes.TryGetValue(name, out var attribute);
        return attribute;
    }

    public void ClearAttribute(string name)
    {
        _attributes.Remove(name);
    }

    public void Close()
    {
        Disconnect();
    }

    public void SendPacket(byte[] packet)
    {
        SendAsync(packet);
    }
}
