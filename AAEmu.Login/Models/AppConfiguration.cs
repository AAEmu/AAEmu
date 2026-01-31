using System.ComponentModel.DataAnnotations;

namespace AAEmu.Login.Models;

/// <summary>
/// Contains general application configuration.
/// </summary>
public class AppConfiguration
{
    /// <summary>
    /// Gets or sets the secret key used to verify game servers when registering themselves with the login server.
    /// </summary>
    [Required]
    public required string SecretKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether accounts should be created automatically if they do not exist.
    /// </summary>
    public bool AutoAccount { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to skip host resolution of game server hostnames.
    /// </summary>
    public bool SkipHostResolve { get; set; }
}
