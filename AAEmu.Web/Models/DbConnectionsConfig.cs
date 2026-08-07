using System.ComponentModel.DataAnnotations;
using AAEmu.Commons.Models;

namespace AAEmu.Web.Models;

/// <summary>
/// Database connection configuration for the web front-end.
/// </summary>
/// <remarks>
/// Two separate databases are involved, matching how the servers themselves are split:
/// account credentials and bans live in the login database, while gameplay values
/// (access level, labor, credits, loyalty) and characters live in the game database.
/// They are joined on <c>users.id</c> == <c>accounts.account_id</c>.
/// <para>
/// Schema updates are owned by AAEmu.Login and AAEmu.Game, so there is deliberately no
/// AutoApplyUpdates equivalent here.
/// </para>
/// </remarks>
public class DbConnectionsConfig
{
    public const string ConfigurationSectionName = "Connections";

    /// <summary>
    /// Gets or sets the connection settings for the login database (<c>aaemu_login</c>).
    /// </summary>
    [Required]
    public required MySqlConnectionSettings MySQLProvider { get; set; }

    /// <summary>
    /// Gets or sets the connection settings for the game database (<c>aaemu_game</c>).
    /// </summary>
    [Required]
    public required MySqlConnectionSettings GameMySQLProvider { get; set; }
}
