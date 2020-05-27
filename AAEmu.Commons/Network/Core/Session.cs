using System.Net;
using System.Net.Sockets;
using AAEmu.Commons.Network.Core.Messages;
using NetCoreServer;

namespace AAEmu.Commons.Network.Core
{
    public class Session : TcpSession
    {
        public IProtocolHandler ProtocolHandler { get; private set; }
        public IPEndPoint RemoteEndPoint { get; private set; }

        public Session(Server server) : base(server)
        {
            ProtocolHandler = server.GetHandler();
        }

        protected override void OnConnected()
        {
            RemoteEndPoint = (IPEndPoint)Socket.RemoteEndPoint;
            ProtocolHandler?.OnConnected(this);
        }

        protected override void OnDisconnected()
        {
            ProtocolHandler?.OnDisconnected(this);
        }

        protected override void OnReceived(byte[] buffer, long offset, long size)
        {
            ProtocolHandler?.OnReceived(this, buffer, offset, size);
        }

        protected override void OnSent(long sent, long pending)
        {
        }

        protected override void OnError(SocketError error)
        {
        }

        public virtual void SendMessage(IWritable message)
        {
            var stream = new PacketStream();
            message.Write(stream);
            SendAsync(stream);
        }

        public override bool SendAsync(byte[] buffer)
        {
            // TODO send to queue
            return SendAsync(buffer, 0L, buffer.Length);
        }
    }
}
