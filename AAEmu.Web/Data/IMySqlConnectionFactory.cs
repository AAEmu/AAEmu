using MySql.Data.MySqlClient;

namespace AAEmu.Web.Data;

public interface IMySqlConnectionFactory
{
    /// <summary>
    /// Creates and opens a new connection to the login database (<c>aaemu_login</c>).
    /// </summary>
    Task<MySqlConnection> CreateLoginConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates and opens a new connection to the game database (<c>aaemu_game</c>).
    /// </summary>
    Task<MySqlConnection> CreateGameConnectionAsync(CancellationToken cancellationToken = default);
}
