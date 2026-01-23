using AAEmu.Commons.Utils.DB;
using AAEmu.Login.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AAEmu.Login.Utils;

public class MySqlInitializer(IOptions<AppConfiguration> appConfig, ILogger<MySqlInitializer> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        MySQL.SetConfiguration(appConfig.Value.Connections.MySQLProvider);

        try
        {
            // Test the DB connection
            await using var connection = MySQL.CreateConnection();
            logger.LogInformation("MySQL connection established successfully to {DataSource}. Server version {Version}",
                connection.DataSource,
                connection.ServerVersion);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "MySQL connection failed, check your configuration!");
            throw;
        }
    }
}
