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

    /// <summary>
    /// Gets or sets whether to automatically apply database schema updates without an interactive prompt.
    /// Intended for unattended environments such as Aspire or CI. Not recommended for production use, as it may apply
    /// updates without proper review.
    /// </summary>
    public bool AutoApplyUpdates { get; set; }
}
