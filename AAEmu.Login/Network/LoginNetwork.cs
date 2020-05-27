using System.Net;
using AAEmu.Commons.DI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AAEmu.Login.Network
{
    public interface ILoginNetwork
    {
        void Start();
        void Stop();
    }

    public class LoginNetwork : ILoginNetwork, ISingletonService
    {
        private ILogger<LoginNetwork> _logger;
        private LoginServer _server;

        public LoginNetwork(IConfiguration configuration, 
            ILogger<LoginNetwork> logger,
            ILogger<LoginServer> loginLogger,
            ILoginProtocolHandler handler)
        {
            var host = IPAddress.Parse(configuration["Network:Host"]);
            var port = int.Parse(configuration["Network:Port"]);
            _logger = logger;
            _server = new LoginServer(host, port, loginLogger, handler);
        }

        public void Start()
        {
            _server.Start();
        }

        public void Stop()
        {
            _server.Stop();
        }
    }
}
