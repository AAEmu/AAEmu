using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using NetCoreServer;
using NLog;

namespace AAEmu.Commons.Network.Core
{
    public class Server : TcpServer
    {
        private Logger _logger = LogManager.GetCurrentClassLogger();
        private BaseProtocolHandler _protocolHandler;
        private readonly HashSet<Session> _sessions = new HashSet<Session>();

        public BaseProtocolHandler GetHandler() => _protocolHandler;

        public Server(IPAddress address, int port, BaseProtocolHandler protocolHandler)
            : base(address, port)
        {
            _protocolHandler = protocolHandler;
        }

        protected override TcpSession CreateSession() => new Session(this);


        protected override void OnStarted()
        {
            _logger.Info($"TCP server listening start on {Endpoint}");
        }

        protected override void OnStopped()
        {
            _logger.Info("TCP server listener stopped!");
        }

        protected override void OnConnected(TcpSession session)
        {
            _logger.Info(
                $"Connect from {session.Socket.RemoteEndPoint} established, session id: {session.Id}");
            _sessions.Add((Session)session);
        }

        protected override void OnDisconnected(TcpSession session)
        {
            _logger.Info($"Connect from session id: {session.Id} disconnected");
            _sessions.Remove((Session)session);
        }

        protected override void OnError(SocketError error)
        {
            _logger.Error($"TCP server SocketError: {error}");
        }

        public Session GetSession(Func<Session, bool> func)
        {
            return _sessions.SingleOrDefault(func);
        }

        public HashSet<Session> GetSessions()
        {
            return _sessions;
        }

        public IEnumerable<Session> GetSessions(Func<Session, bool> func)
        {
            return _sessions.Where(func);
        }
    }
}
