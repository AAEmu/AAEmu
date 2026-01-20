using MySql.Data.MySqlClient;

namespace AAEmu.Login.Utils;

public interface IMySqlConnectionFactory
{
    /// <summary>
    /// Creates and opens a new MySQL database connection.
    /// </summary>
    MySqlConnection CreateConnection();
}
