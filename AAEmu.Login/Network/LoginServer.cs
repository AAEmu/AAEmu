using System.Net;
using System.Net.Sockets;
using AAEmu.Commons.Network.Core;
using Microsoft.Extensions.Logging;
using NetCoreServer;

namespace AAEmu.Login.Network
{
    public class LoginServer : Server
    {
        private ILogger<LoginServer> _logger;

        public LoginServer(IPAddress address, int port, ILogger<LoginServer> logger, ILoginProtocolHandler handler) 
            : base(address, port, logger, handler)
        {
            _logger = logger;
        }

        protected override TcpSession CreateSession() => new LoginSession(this);

        protected override void OnError(SocketError error)
        {
            _logger.LogError($"SocketError: {error}");
        }
    }
}
