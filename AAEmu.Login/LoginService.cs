using AAEmu.Commons.Utils.DB;
using AAEmu.Commons.Utils.Updater;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NLog;

namespace AAEmu.Login;

public sealed class LoginService(
    IGameController gameController,
    IRequestController requestController,
    IInternalNetwork internalNetwork,
    ILoginNetwork loginNetwork,
    IOptions<AppConfiguration> appConfig) : IHostedService, IDisposable
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Logger.Info("Starting daemon: AAEmu.Login");
        // Check for updates
        using (var connection = MySQL.CreateConnection())
        {
            if (!MySqlDatabaseUpdater.Run(connection, "aaemu_login",
                    appConfig.Value.Connections.MySQLProvider.Database))
            {
                Logger.Fatal("Failed up update database !");
                Logger.Fatal("Press Ctrl+C to quit");
                return Task.CompletedTask;
            }
        }

        requestController.Initialize();
        gameController.Load();
        loginNetwork.Start();
        internalNetwork.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Logger.Info("Stopping daemon.");
        loginNetwork.Stop();
        internalNetwork.Stop();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Logger.Info("Disposing....");
        LogManager.Flush();
    }
}
