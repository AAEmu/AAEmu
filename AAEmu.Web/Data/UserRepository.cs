using AAEmu.Web.Models;
using MySql.Data.MySqlClient;

namespace AAEmu.Web.Data;

public interface IUserRepository
{
    Task<int> GetAccountCountAsync(CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<UserSummary> Users, int TotalCount)> GetUsersAsync(
        string? search, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<UserSummary?> GetUserAsync(uint id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves usernames for a set of account ids. Used to label characters, which live in the
    /// other database and so cannot be joined in SQL.
    /// </summary>
    Task<IReadOnlyDictionary<uint, string>> GetUsernamesAsync(IReadOnlyCollection<uint> ids,
        CancellationToken cancellationToken = default);

    Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken = default);

    Task<uint> CreateUserAsync(string username, string email, string passwordHash, string registrationIp,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads and writes the login database's <c>users</c> table directly. Kept to raw SQL to match the
/// data access style used by AAEmu.Login and AAEmu.Game.
/// </summary>
public class UserRepository(IMySqlConnectionFactory connectionFactory) : IUserRepository
{
    public async Task<int> GetAccountCountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateLoginConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CAST(COUNT(*) AS SIGNED) FROM users";

        return (int)Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<(IReadOnlyList<UserSummary> Users, int TotalCount)> GetUsersAsync(
        string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var hasSearch = !string.IsNullOrWhiteSpace(search);
        var filter = hasSearch ? " WHERE username LIKE @search" : string.Empty;

        await using var connection = await connectionFactory.CreateLoginConnectionAsync(cancellationToken);

        int totalCount;
        await using (var countCommand = connection.CreateCommand())
        {
            countCommand.CommandText = $"SELECT CAST(COUNT(*) AS SIGNED) FROM users{filter}";
            if (hasSearch)
                countCommand.Parameters.AddWithValue("@search", $"%{EscapeLike(search!)}%");

            totalCount = (int)Convert.ToInt64(await countCommand.ExecuteScalarAsync(cancellationToken));
        }

        if (totalCount == 0)
            return ([], 0);

        var users = new List<UserSummary>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                $"""
                 SELECT id, username, email, created_at, last_login, banned, ban_reason
                 FROM users{filter}
                 ORDER BY id DESC
                 LIMIT @limit OFFSET @offset
                 """;
            if (hasSearch)
                command.Parameters.AddWithValue("@search", $"%{EscapeLike(search!)}%");
            command.Parameters.AddWithValue("@limit", pageSize);
            command.Parameters.AddWithValue("@offset", (page - 1) * pageSize);

            await using var reader = (MySqlDataReader)await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                users.Add(ReadUser(reader));
            }
        }

        return (users, totalCount);
    }

    public async Task<UserSummary?> GetUserAsync(uint id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateLoginConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, username, email, created_at, last_login, banned, ban_reason
            FROM users
            WHERE id = @id
            """;
        command.Parameters.AddWithValue("@id", id);

        await using var reader = (MySqlDataReader)await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadUser(reader) : null;
    }

    public async Task<IReadOnlyDictionary<uint, string>> GetUsernamesAsync(IReadOnlyCollection<uint> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
            return new Dictionary<uint, string>();

        await using var connection = await connectionFactory.CreateLoginConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        // Ids come from the characters table, not from user input, but they are still bound as
        // parameters rather than interpolated into the IN list.
        var parameterNames = new List<string>(ids.Count);
        var index = 0;
        foreach (var id in ids)
        {
            var name = $"@id{index++}";
            parameterNames.Add(name);
            command.Parameters.AddWithValue(name, id);
        }

        command.CommandText = $"SELECT id, username FROM users WHERE id IN ({string.Join(", ", parameterNames)})";

        var usernames = new Dictionary<uint, string>();
        await using var reader = (MySqlDataReader)await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            usernames[reader.GetUInt32("id")] = reader.GetString("username");
        }

        return usernames;
    }

    private static UserSummary ReadUser(MySqlDataReader reader)
    {
        var lastLogin = reader.GetInt64("last_login");
        return new UserSummary
        {
            Id = reader.GetUInt32("id"),
            Username = reader.GetString("username"),
            Email = reader.IsDBNull(reader.GetOrdinal("email")) ? string.Empty : reader.GetString("email"),
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64("created_at")),
            LastLogin = lastLogin > 0 ? DateTimeOffset.FromUnixTimeSeconds(lastLogin) : null,
            Banned = reader.GetUInt32("banned") != 0,
            BanReason = reader.GetUInt32("ban_reason")
        };
    }

    public async Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateLoginConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM users WHERE username = @username LIMIT 1";
        command.Parameters.AddWithValue("@username", username);

        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async Task<uint> CreateUserAsync(string username, string email, string passwordHash, string registrationIp,
        CancellationToken cancellationToken = default)
    {
        // The users table has no unique index on username, so this insert is guarded by a prior
        // existence check rather than by the database. See the note in Pages/Accounts/Create.cshtml.cs.
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await using var connection = await connectionFactory.CreateLoginConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO users (username, password, email, last_ip, last_login, created_at, updated_at)
            VALUES (@username, @password, @email, @last_ip, 0, @created_at, @updated_at)
            """;
        command.Parameters.AddWithValue("@username", username);
        command.Parameters.AddWithValue("@password", passwordHash);
        command.Parameters.AddWithValue("@email", email);
        command.Parameters.AddWithValue("@last_ip", registrationIp);
        command.Parameters.AddWithValue("@created_at", nowUnix);
        command.Parameters.AddWithValue("@updated_at", nowUnix);

        await command.ExecuteNonQueryAsync(cancellationToken);
        return (uint)command.LastInsertedId;
    }

    /// <summary>
    /// Escapes LIKE wildcards so a search for "100%" does not match everything.
    /// </summary>
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
