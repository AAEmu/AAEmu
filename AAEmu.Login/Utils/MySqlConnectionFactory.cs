using AAEmu.Login.Models;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

namespace AAEmu.Login.Utils;

public class MySqlConnectionFactory(IConfiguration configuration, IOptions<DBConnectionsConfig> dbConnectionsConfig)
    : IMySqlConnectionFactory
{
    private readonly string _connectionString =
        configuration.GetConnectionString("MySqlConnection") ?? CreateConnectionString(dbConnectionsConfig);

    public MySqlConnection CreateConnection()
    {
        MySqlConnection? connection = null;
        try
        {
            connection = new MySqlConnection(_connectionString);
            connection.Open();
            return connection;
        }
        catch (Exception)
        {
            connection?.Dispose();
            throw;
        }
    }

    private static string CreateConnectionString(IOptions<DBConnectionsConfig> configuration)
    {
        var options = configuration.Value.MySQLProvider;
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
            DefaultCommandTimeout = 180
        };
        return connectionStringBuilder.ConnectionString;
    }
}
