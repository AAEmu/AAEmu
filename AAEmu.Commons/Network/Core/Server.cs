using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using NetCoreServer;

namespace AAEmu.Commons.Network.Core
{
    public class Server : TcpServer
    {
        private ILogger<Server> _logger;
        private IProtocolHandler _protocolHandler;
        private readonly HashSet<Session> _sessions = new HashSet<Session>();

        public IProtocolHandler GetHandler() => _protocolHandler;

        public Server(IPAddress address, int port, ILogger<Server> logger, IProtocolHandler protocolHandler)
            : base(address, port)
        {
            _logger = logger;
            _protocolHandler = protocolHandler;
        }

        protected override TcpSession CreateSession() => new Session(this);


        protected override void OnStarted()
        {
            _logger.LogInformation($"TCP server listening start on {Endpoint}");
        }

        protected override void OnStopped()
        {
            _logger.LogInformation("TCP server listener stopped!");
        }

        protected override void OnConnected(TcpSession session)
        {
            _logger.LogInformation(
                $"Connect from {session.Socket.RemoteEndPoint} established, session id: {session.Id}");
            _sessions.Add((Session)session);
        }

        protected override void OnDisconnected(TcpSession session)
        {
            _logger.LogInformation($"Connect from session id: {session.Id} disconnected");
            _sessions.Remove((Session)session);
        }

        protected override void OnError(SocketError error)
        {
            _logger.LogError($"TCP server SocketError: {error}");
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
