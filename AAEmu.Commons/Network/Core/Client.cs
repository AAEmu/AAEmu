using System.Net;
using NetCoreServer;

namespace AAEmu.Commons.Network.Core
{
    public class Client : TcpClient
    {
        private BaseProtocolHandler _handler;
        private Session _session;
        
        public Client(IPAddress address, int port, BaseProtocolHandler handler) : base(address, port)
        {
            _handler = handler;
            _session = new Session(this);
        }

        public BaseProtocolHandler GetHandler()
        {
            return _handler;
        }

        protected override void OnConnected()
        {
            _handler.OnConnect(_session);
        }

        protected override void OnDisconnected()
        {
            _handler.OnDisconnect(_session);
        }

        protected override void OnReceived(byte[] buffer, long offset, long size)
        {
            _handler.OnReceive(_session, buffer, (int) size);
        }

        protected override void OnSent(long sent, long pending)
        {
            base.OnSent(sent, pending);
        }
    }
}
