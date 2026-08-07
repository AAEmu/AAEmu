namespace AAEmu.Web.Models;

/// <summary>
/// A row of the game database's <c>accounts</c> table — the per-account values that are shared by
/// every character on that account.
/// </summary>
/// <remarks>
/// The game server creates this row lazily, on the account's first login
/// (<c>AccountManager.GetAccountDetailsInternal</c>), so an account that has registered but never
/// played has no row here yet.
/// </remarks>
public sealed class GameAccount
{
    /// <summary>Matches <c>users.id</c> in the login database.</summary>
    public required uint AccountId { get; init; }

    public required int AccessLevel { get; init; }
    public required int Labor { get; init; }
    public required int Credits { get; init; }
    public required int Loyalty { get; init; }
    public required DateTime LastUpdated { get; init; }
    public required DateTime LastLogin { get; init; }

    /// <summary>
    /// The game server reads labor with <c>MySqlDataReader.GetInt16</c>
    /// (<c>AccountManager.cs</c>), so a value beyond <see cref="short.MaxValue"/> would overflow
    /// on its next account load even though the column itself is an INT.
    /// </summary>
    public const int MaxLabor = short.MaxValue;
}
