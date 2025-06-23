using AAEmu.Commons.Utils.DB;
using AAEmu.Commons.Utils.Updater;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;
using AAEmu.Login.Models.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NLog;

namespace AAEmu.Login;

public sealed class LoginService(
    IGameController gameController,
    IRequestController requestController,
    IInternalNetwork internalNetwork,
    ILoginNetwork loginNetwork,
    IOptions<AppConfiguration> appConfig,
    IDbContextFactory<LoginDbContext> dbContextFactory) : IHostedService, IDisposable
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public async Task StartAsync(CancellationToken cancellationToken)
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
                return;
            }
        }

        // Apply EF Core migrations after the old-style updates
        Logger.Debug("Performing EF Core migrations...");
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            // Ensure database is created and migrations are applied
            await dbContext.Database.MigrateAsync(cancellationToken: cancellationToken);
        }
        Logger.Debug("EF Core migrations done");

        requestController.Initialize();
        gameController.Load();
        loginNetwork.Start();
        internalNetwork.Start();
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
