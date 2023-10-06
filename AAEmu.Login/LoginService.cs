using System;
using System.Threading;
using System.Threading.Tasks;
using AAEmu.Commons.Utils.DB;
using AAEmu.Commons.Utils.Updater;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;
using Microsoft.Extensions.Hosting;
using NLog;

namespace AAEmu.Login;

public sealed class LoginService : IHostedService, IDisposable
{
    private static Logger Logger = LogManager.GetCurrentClassLogger();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Logger.Info("Starting daemon: AAEmu.Login");
        // Check for updates
        using (var connection = MySQL.CreateConnection())
        {
            if (!MySqlDatabaseUpdater.Run(connection, "aaemu_login", AppConfiguration.Instance.Connections.MySQLProvider.Database))
            {
                Logger.Fatal("Failed up update database !");
                Logger.Fatal("Press Ctrl+C to quit");
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
        Logger.Info("Stopping daemon.");
        LoginNetwork.Instance?.Stop();
        InternalNetwork.Instance?.Stop();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Logger.Info("Disposing....");
        LogManager.Flush();
    }
}
