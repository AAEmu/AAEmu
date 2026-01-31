using System.ComponentModel.DataAnnotations;

namespace AAEmu.Login.Models;

/// <summary>
/// A base class for network configuration settings.
/// </summary>
public abstract class NetworkConfig
{
    /// <summary>
    /// Gets or sets the host address to listen on.
    /// </summary>
    [Required]
    public required string Host { get; set; }

    /// <summary>
    /// Gets or sets the port number to listen on.
    /// </summary>
    [Required]
    public required ushort Port { get; set; }

    /// <summary>
    /// Gets or sets the number of simultaneous connections allowed.
    /// </summary>
    public required int NumConnections { get; set; }
}
