using AAEmu.Commons.Utils.DB;
using AAEmu.Login.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NLog;

namespace AAEmu.Login.Utils;

public class MySqlInitializer(IOptions<AppConfiguration> appConfig) : BackgroundService
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        MySQL.SetConfiguration(appConfig.Value.Connections.MySQLProvider);

        try
        {
            // Test the DB connection
            await using var connection = MySQL.CreateConnection();
            Logger.Info("MySQL connection established successfully to {0}. Server version {1}", connection.DataSource,
                connection.ServerVersion);
        }
        catch (Exception ex)
        {
            Logger.Fatal(ex, "MySQL connection failed, check your configuration!");
            LogManager.Flush();
            throw;
        }
    }
}
