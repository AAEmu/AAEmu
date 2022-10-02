using System;
using System.Threading;
using System.Threading.Tasks;
using AAEmu.Commons.Utils.Updater;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;
using AAEmu.Commons.Utils.DB;
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
            // Check for updates
            using (var connection = MySQL.CreateConnection())
            {
                if (!MySqlDatabaseUpdater.Run(connection, "aaemu_login", AppConfiguration.Instance.Connections.MySQLProvider.Database))
                {
                    _log.Fatal("Failed up update database !");
                    _log.Fatal("Press Ctrl+C to quit");
                    return Task.CompletedTask;
                }
            }
            RequestController.Instance.Initialize();
            GameController.Instance.Load();
            LoginNetwork.Instance.Start();
            InternalNetwork.Instance.Start();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _log.Info("Stopping daemon.");
            LoginNetwork.Instance?.Stop();
            InternalNetwork.Instance?.Stop();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _log.Info("Disposing....");
            LogManager.Flush();
        }
    }
}
