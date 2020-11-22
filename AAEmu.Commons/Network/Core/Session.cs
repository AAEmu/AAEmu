using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using NetCoreServer;

namespace AAEmu.Commons.Network.Core
{
    public class Session: TcpSession
    {
        public BaseProtocolHandler ProtocolHandler { get; private set; }
        public IPEndPoint RemoteEndPoint { get; set; }
        private readonly Dictionary<string, object> _attributes = new Dictionary<string, object>();
        public uint Id { get; set; }
        public IPAddress Ip { get; private set; }
        
        public Client Client { get; set; }

        public Session(Server server) : base(server)
        {
            ProtocolHandler = server.GetHandler();
        }

        public Session(Client client) : base(null)
        {
            Client = client;
            ProtocolHandler = client.GetHandler();
            Ip = client.Endpoint.Address;
            Id = (uint) client.Endpoint.GetHashCode();
        }

        protected override void OnConnected()
        {
            RemoteEndPoint = (IPEndPoint)Socket.RemoteEndPoint;
            Id = (uint)RemoteEndPoint.GetHashCode();
            Ip = RemoteEndPoint.Address;
            ProtocolHandler?.OnConnect(this);
        }

        protected override void OnDisconnected()
        {
            ProtocolHandler?.OnDisconnect(this);
        }

        protected override void OnReceived(byte[] buffer, long offset, long size)
        {
            ProtocolHandler?.OnReceive(this, buffer, (int) size);
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
}
