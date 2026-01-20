namespace AAEmu.Login.Utils;

public class MySqlInitializer(IMySqlConnectionFactory connectionFactory, ILogger<MySqlInitializer> logger)
    : IHostedService
{
    // Not using BackgroundService here due to it not preventing other hosted services from starting:
    // https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/10.0/backgroundservice-executeasync-task
    // This service needs to run before any other service that relies on the database.

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Test the DB connection
            await using var connection = connectionFactory.CreateConnection();
            logger.LogInformation(
                "MySQL connection established successfully to {DataSource}. Server version {ServerVersion}",
                connection.DataSource, connection.ServerVersion);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "MySQL connection failed, check your configuration!");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
