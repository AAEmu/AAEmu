namespace AAEmu.Login.Models.Database;

/// <summary>
/// Account login information
/// </summary>
public partial class User
{
    public AccountId Id { get; set; }

    public required string Username { get; set; }

    /// <summary>
    /// Hashed password of the user
    /// </summary>
    public required string Password { get; set; }

    public required string Email { get; set; }

    public DateTime LastLogin { get; set; }

    public required string LastIp { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public bool Banned { get; set; }

    public byte BanReason { get; set; }
}
