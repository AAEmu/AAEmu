using AAEmu.Commons.Models;
using AAEmu.Web.Models;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace AAEmu.Web.Data;

public class MySqlConnectionFactory(IConfiguration configuration, IOptions<DbConnectionsConfig> dbConnectionsConfig)
    : IMySqlConnectionFactory
{
    private readonly string _loginConnectionString =
        configuration.GetConnectionString("MySqlConnection")
        ?? CreateConnectionString(dbConnectionsConfig.Value.MySQLProvider);

    private readonly string _gameConnectionString =
        configuration.GetConnectionString("GameMySqlConnection")
        ?? CreateConnectionString(dbConnectionsConfig.Value.GameMySQLProvider);

    public Task<MySqlConnection> CreateLoginConnectionAsync(CancellationToken cancellationToken = default) =>
        OpenAsync(_loginConnectionString, cancellationToken);

    public Task<MySqlConnection> CreateGameConnectionAsync(CancellationToken cancellationToken = default) =>
        OpenAsync(_gameConnectionString, cancellationToken);

    private static async Task<MySqlConnection> OpenAsync(string connectionString,
        CancellationToken cancellationToken)
    {
        MySqlConnection? connection = null;
        try
        {
            connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch (Exception)
        {
            if (connection is not null)
            {
                await connection.DisposeAsync();
            }

            throw;
        }
    }

    private static string CreateConnectionString(MySqlConnectionSettings options)
    {
        var connectionStringBuilder = new MySqlConnectionStringBuilder
        {
            Server = options.Host,
            Port = options.Port,
            UserID = options.User,
            Password = options.Password,
            Database = options.Database,
            SslMode = MySqlSslMode.Preferred,
            Pooling = true,
            MinimumPoolSize = 0,
            MaximumPoolSize = 10,
            ConnectionLifeTime = 600,
            CharacterSet = "utf8",
            AllowZeroDateTime = true,
            ConvertZeroDateTime = true,
            DefaultCommandTimeout = 30
        };
        return connectionStringBuilder.ConnectionString;
    }
}
