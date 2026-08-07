namespace AAEmu.Web.Models;

/// <summary>
/// A read-only projection of a row in the login database's <c>users</c> table.
/// </summary>
public sealed class UserSummary
{
    public required uint Id { get; init; }
    public required string Username { get; init; }
    public required string Email { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Last successful login, or <c>null</c> when the account has never logged in
    /// (stored as 0 rather than NULL in the database).
    /// </summary>
    public required DateTimeOffset? LastLogin { get; init; }

    public required bool Banned { get; init; }
    public required uint BanReason { get; init; }

    /// <summary>
    /// Returns the email with the local part partially hidden, so the account list does not
    /// hand out full addresses.
    /// </summary>
    public string MaskedEmail
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Email))
                return "—";

            var at = Email.IndexOf('@');
            if (at <= 0)
                return "•••";

            var local = Email[..at];
            var domain = Email[at..];
            var visible = local.Length <= 2 ? local[..1] : local[..2];
            return $"{visible}{new string('•', Math.Min(6, Math.Max(1, local.Length - visible.Length)))}{domain}";
        }
    }
}
