using AAEmu.Commons.Utils.DB;
using AAEmu.Commons.Utils.Updater;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Models;
using Microsoft.Extensions.Options;

namespace AAEmu.Login;

public sealed class LoginService(
    IGameController gameController,
    IRequestController requestController,
    IInternalNetwork internalNetwork,
    IOptions<DBConnectionsConfig> dbConnectionsConfig,
    ILogger<LoginService> logger) : IHostedService, IDisposable
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting daemon: AAEmu.Login");

        // Check for updates
        await using (var connection = MySQL.CreateConnection())
        {
            if (!MySqlDatabaseUpdater.Run(connection, "aaemu_login",
                    dbConnectionsConfig.Value.MySQLProvider.Database))
            {
                logger.LogCritical("Failed to update database!");
                logger.LogCritical("Press Ctrl+C to quit");
                return;
            }
        }

        requestController.Initialize();
        gameController.Load();
        internalNetwork.Start();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping daemon.");
        internalNetwork.Stop();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        logger.LogInformation("Disposing...");
    }
}
