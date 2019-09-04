using System;
using System.Threading;
using System.Threading.Tasks;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Core.Network.Login;
using Microsoft.Extensions.Hosting;
using NLog;

namespace AAEmu.Login
{
    public class LoginService : IHostedService, IDisposable
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _log.Info("Starting daemon: AAEmu.Login");
            RequestController.Instance.Initialize();
            GameController.Instance.Load();
            LoginNetwork.Instance.Start();
            InternalNetwork.Instance.Start();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _log.Info("Stopping daemon.");
            LoginNetwork.Instance.Stop();
            InternalNetwork.Instance.Stop();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _log.Info("Disposing....");
            LogManager.Flush();
        }
    }
}
