using System;
using System.Threading;
using System.Threading.Tasks;
using AAEmu.Commons.Utils;
using AAEmu.Login.Login;
using AAEmu.Login.Models;
using AAEmu.Login.Network;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SlimMessageBus;

namespace AAEmu.Login
{
    public class LoginService : IHostedService
    {
        private readonly ILogger<LoginService> _logger;
        private readonly AuthContext _context;
        private readonly IServerManager _manager;
        private readonly ILoginNetwork _network;
        private readonly IMessageBus _bus;

        public IConfiguration Configuration { get; }

        public LoginService(IConfiguration configuration, ILogger<LoginService> logger, AuthContext context,
            IServerManager manager, ILoginNetwork network, IMessageBus bus)
        {
            Configuration = configuration;
            _logger = logger;
            _context = context;
            _manager = manager;
            _network = network;
            _bus = bus;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                _context.CanConnect();
                _manager.Initialize();

                _network.Start();

                return Task.CompletedTask;
            }
            catch (Exception e)
            {
                return Task.FromException(e);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _network.Stop();

                return Task.CompletedTask;
            }
            catch (Exception e)
            {
                return Task.FromException(e);
            }
        }
    }
}
