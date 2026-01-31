using System.ComponentModel.DataAnnotations;
using AAEmu.Commons.Models;

namespace AAEmu.Login.Models;

/// <summary>
/// Contains database connection configuration settings.
/// </summary>
public class DBConnectionsConfig
{
    public const string ConfigurationSectionName = "Connections";

    /// <summary>
    /// Gets or sets the MySQL database connection settings.
    /// </summary>
    [Required]
    public required MySqlConnectionSettings MySQLProvider { get; set; }
}
