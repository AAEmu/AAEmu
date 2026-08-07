using AAEmu.Web.Models;
using MySql.Data.MySqlClient;

namespace AAEmu.Web.Data;

public interface IGameRepository
{
    /// <summary>
    /// Returns the <c>accounts</c> row for an account, or null when the account has never logged in.
    /// </summary>
    Task<GameAccount?> GetAccountAsync(uint accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the editable per-account values, creating the row if the account has never logged in.
    /// </summary>
    Task UpdateAccountAsync(uint accountId, int accessLevel, int labor, int credits, int loyalty,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterSummary>> GetCharactersByAccountAsync(uint accountId,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<CharacterSummary> Characters, int TotalCount)> SearchCharactersAsync(
        string? search, bool includeDeleted, int page, int pageSize, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads and writes the game database (<c>aaemu_game</c>) — the <c>accounts</c> and
/// <c>characters</c> tables.
/// </summary>
public class GameRepository(IMySqlConnectionFactory connectionFactory) : IGameRepository
{
    private const string CharacterColumns =
        """
        id, account_id, name, access_level, race, gender, level, experience, money, aa_point,
        honor_point, faction_id, faction_name, world_id, zone_id, created_at, deleted, total_play_time
        """;

    public async Task<GameAccount?> GetAccountAsync(uint accountId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateGameConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT account_id, access_level, labor, credits, loyalty, last_updated, last_login
            FROM accounts
            WHERE account_id = @account_id
            """;
        command.Parameters.AddWithValue("@account_id", accountId);

        await using var reader = (MySqlDataReader)await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new GameAccount
        {
            AccountId = reader.GetUInt32("account_id"),
            AccessLevel = reader.GetInt32("access_level"),
            Labor = reader.GetInt32("labor"),
            Credits = reader.GetInt32("credits"),
            Loyalty = reader.GetInt32("loyalty"),
            LastUpdated = reader.GetDateTime("last_updated"),
            LastLogin = reader.GetDateTime("last_login")
        };
    }

    public async Task UpdateAccountAsync(uint accountId, int accessLevel, int labor, int credits, int loyalty,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateGameConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        // Upsert, because the game server only creates this row on the account's first login.
        // last_updated is maintained by the update_timestamps trigger on the table.
        command.CommandText =
            """
            INSERT INTO accounts (account_id, access_level, labor, credits, loyalty)
            VALUES (@account_id, @access_level, @labor, @credits, @loyalty)
            ON DUPLICATE KEY UPDATE
                access_level = @access_level,
                labor = @labor,
                credits = @credits,
                loyalty = @loyalty
            """;
        command.Parameters.AddWithValue("@account_id", accountId);
        command.Parameters.AddWithValue("@access_level", accessLevel);
        command.Parameters.AddWithValue("@labor", labor);
        command.Parameters.AddWithValue("@credits", credits);
        command.Parameters.AddWithValue("@loyalty", loyalty);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CharacterSummary>> GetCharactersByAccountAsync(uint accountId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.CreateGameConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             SELECT {CharacterColumns}
             FROM characters
             WHERE account_id = @account_id
             ORDER BY deleted, level DESC, name
             """;
        command.Parameters.AddWithValue("@account_id", accountId);

        await using var reader = (MySqlDataReader)await command.ExecuteReaderAsync(cancellationToken);
        return await ReadCharactersAsync(reader, cancellationToken);
    }

    public async Task<(IReadOnlyList<CharacterSummary> Characters, int TotalCount)> SearchCharactersAsync(
        string? search, bool includeDeleted, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var hasSearch = !string.IsNullOrWhiteSpace(search);

        var conditions = new List<string>();
        if (hasSearch)
            conditions.Add("name LIKE @search");
        if (!includeDeleted)
            conditions.Add("deleted = 0");

        var filter = conditions.Count > 0 ? $" WHERE {string.Join(" AND ", conditions)}" : string.Empty;

        await using var connection = await connectionFactory.CreateGameConnectionAsync(cancellationToken);

        int totalCount;
        await using (var countCommand = connection.CreateCommand())
        {
            countCommand.CommandText = $"SELECT CAST(COUNT(*) AS SIGNED) FROM characters{filter}";
            if (hasSearch)
                countCommand.Parameters.AddWithValue("@search", $"%{EscapeLike(search!)}%");

            totalCount = (int)Convert.ToInt64(await countCommand.ExecuteScalarAsync(cancellationToken));
        }

        if (totalCount == 0)
            return ([], 0);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                $"""
                 SELECT {CharacterColumns}
                 FROM characters{filter}
                 ORDER BY level DESC, name
                 LIMIT @limit OFFSET @offset
                 """;
            if (hasSearch)
                command.Parameters.AddWithValue("@search", $"%{EscapeLike(search!)}%");
            command.Parameters.AddWithValue("@limit", pageSize);
            command.Parameters.AddWithValue("@offset", (page - 1) * pageSize);

            await using var reader = (MySqlDataReader)await command.ExecuteReaderAsync(cancellationToken);
            return (await ReadCharactersAsync(reader, cancellationToken), totalCount);
        }
    }

    private static async Task<IReadOnlyList<CharacterSummary>> ReadCharactersAsync(MySqlDataReader reader,
        CancellationToken cancellationToken)
    {
        var characters = new List<CharacterSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            characters.Add(new CharacterSummary
            {
                Id = reader.GetUInt32("id"),
                AccountId = reader.GetUInt32("account_id"),
                Name = reader.GetString("name"),
                AccessLevel = reader.GetInt32("access_level"),
                Race = (Race)reader.GetByte("race"),
                Gender = (Gender)reader.GetByte("gender"),
                Level = reader.GetByte("level"),
                Experience = reader.GetInt32("experience"),
                Money = reader.GetInt64("money"),
                AaPoint = reader.GetInt64("aa_point"),
                HonorPoint = reader.GetInt32("honor_point"),
                FactionId = reader.GetUInt32("faction_id"),
                FactionName = reader.GetString("faction_name"),
                WorldId = reader.GetUInt32("world_id"),
                ZoneId = reader.GetUInt32("zone_id"),
                CreatedAt = reader.GetDateTime("created_at"),
                Deleted = reader.GetInt32("deleted") != 0,
                TotalPlayTime = reader.GetUInt32("total_play_time")
            });
        }

        return characters;
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
