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

    /// <summary>
    /// Timeout for the game server to respond to an EnterWorld request.
    /// </summary>
    public TimeSpan EnterWorldTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Maximum time to wait for game servers to return character summaries for the world list.</summary>
    public TimeSpan WorldListRequestTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Character-slot values published by the login protocol after authentication.
    /// </summary>
    public CharacterSlotConfig CharacterSlots { get; set; } = new();

    [Required]
    public required List<GameServerConfig> GameServers { get; set; }

    /// <summary>
    /// Login-protocol character slot settings. All four values are independent unsigned bytes on the wire.
    /// </summary>
    public class CharacterSlotConfig
    {
        /// <summary>Slots available to the account in ACAuthResponse.</summary>
        [Range(1, byte.MaxValue)]
        public byte AvailableSlots { get; set; } = 6;

        /// <summary>Base creatable character count in ACJoinResponse AFS byte 0.</summary>
        [Range(1, byte.MaxValue)]
        public byte CountLimit { get; set; } = 6;

        /// <summary>Maximum characters per account in ACJoinResponse AFS byte 1.</summary>
        [Range(1, byte.MaxValue)]
        public byte MaxCountLimit { get; set; } = 6;

        /// <summary>Maximum characters per world in ACJoinResponse AFS byte 2.</summary>
        [Range(1, byte.MaxValue)]
        public byte WorldLimit { get; set; } = 6;
    }

    /// <summary>
    /// Contains configuration settings for a single game server.
    /// </summary>
    public class GameServerConfig
    {
        /// <summary>
        /// Gets or sets the unique identifier of the game server.
        /// </summary>
        public required byte Id { get; set; }

        /// <summary>
        /// Gets or sets the display name of the game server, as shown on the client's server selection screen.
        /// </summary>
        public required string Name { get; set; }

        /// <summary>
        /// Gets or sets the host address (IP address or domain name) of the game server.
        /// This address must be accessible to clients.
        /// </summary>
        public required string Host { get; set; }

        /// <summary>
        /// Gets or sets the port number on which the game server listens for incoming connections from clients.
        /// </summary>
        public required ushort Port { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the game server should be hidden from the client's server selection
        /// screen.
        /// </summary>
        public bool Hidden { get; set; }
    }
}
