using System.Collections.Concurrent;
using System.IO;

using AAEmu.Commons.Utils.DB;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Named sentinels for <c>SCContentRosterDelete.result</c>. The client's numeric result mapping
/// was not recovered from the shipped client, so only the names are pinned here.
/// </summary>
public enum ContentRosterDeleteResult : byte
{
    Success = 0,
    InvalidRequest = 1,
    UnknownRoster = 2,
    NotOwner = 3,
    Cooldown = 4,
}

public sealed record ContentRosterDeleteOutcome(ContentRosterDeleteResult Result, int DeletedCount)
{
    public bool Success => Result == ContentRosterDeleteResult.Success;

    public ErrorMessageType Error => Result switch
    {
        ContentRosterDeleteResult.Success => ErrorMessageType.NoErrorMessage,
        ContentRosterDeleteResult.UnknownRoster => ErrorMessageType.ContentRosterNotFound,
        _ => ErrorMessageType.ContentRosterDeleteFailed
    };
}

/// <summary>Account-scoped content-roster rows (<c>account_content_rosters</c>).</summary>
public interface IContentRosterStore
{
    /// <summary>Roster ids that exist at all, used to tell an unknown id from a foreign one.</summary>
    IReadOnlySet<ulong> QueryExisting(IReadOnlyList<ulong> rosterIds);

    /// <summary>The subset of <paramref name="rosterIds"/> owned by <paramref name="accountId"/>.</summary>
    IReadOnlySet<ulong> QueryOwned(ulong accountId, IReadOnlyList<ulong> rosterIds);

    /// <summary>Removes owned rows; returns how many rows actually went away.</summary>
    int DeleteOwned(ulong accountId, IReadOnlyList<ulong> rosterIds);

    /// <summary>Inserts one saved roster and returns its id. 0 when the write failed.</summary>
    ulong Insert(ulong accountId, string title, DateTime createdAt);
}

public sealed class MySqlContentRosterStore : IContentRosterStore
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly Func<MySqlConnection> _connectionFactory;

    public MySqlContentRosterStore() : this(MySQL.CreateConnection)
    {
    }

    internal MySqlContentRosterStore(Func<MySqlConnection> connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public IReadOnlySet<ulong> QueryExisting(IReadOnlyList<ulong> rosterIds)
    {
        var ids = DistinctIds(rosterIds);
        if (ids.Count == 0)
            return new HashSet<ulong>();

        using var connection = _connectionFactory();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT id FROM account_content_rosters WHERE id IN ({Placeholders(ids.Count)})";
        for (var i = 0; i < ids.Count; i++)
            command.Parameters.AddWithValue($"@p{i}", ids[i]);

        var found = new HashSet<ulong>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            found.Add(Convert.ToUInt64(reader.GetValue(0)));
        return found;
    }

    public IReadOnlySet<ulong> QueryOwned(ulong accountId, IReadOnlyList<ulong> rosterIds)
    {
        var ids = DistinctIds(rosterIds);
        if (ids.Count == 0)
            return new HashSet<ulong>();

        using var connection = _connectionFactory();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT id FROM account_content_rosters WHERE account_id = @account_id AND id IN ({Placeholders(ids.Count)})";
        command.Parameters.AddWithValue("@account_id", accountId);
        for (var i = 0; i < ids.Count; i++)
            command.Parameters.AddWithValue($"@p{i}", ids[i]);

        var found = new HashSet<ulong>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            found.Add(Convert.ToUInt64(reader.GetValue(0)));
        return found;
    }

    public int DeleteOwned(ulong accountId, IReadOnlyList<ulong> rosterIds)
    {
        var ids = DistinctIds(rosterIds);
        if (ids.Count == 0)
            return 0;

        using var connection = _connectionFactory();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"DELETE FROM account_content_rosters WHERE account_id = @account_id AND id IN ({Placeholders(ids.Count)})";
        command.Parameters.AddWithValue("@account_id", accountId);
        for (var i = 0; i < ids.Count; i++)
            command.Parameters.AddWithValue($"@p{i}", ids[i]);

        return command.ExecuteNonQuery();
    }

    public ulong Insert(ulong accountId, string title, DateTime createdAt)
    {
        try
        {
            using var connection = _connectionFactory();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO account_content_rosters (account_id, save_title, created_at)
                VALUES (@account_id, @save_title, @created_at)
                """;
            command.Parameters.AddWithValue("@account_id", accountId);
            command.Parameters.AddWithValue("@save_title", title ?? string.Empty);
            command.Parameters.AddWithValue("@created_at", new DateTimeOffset(ServerCalendar.AsUtc(createdAt)).ToUnixTimeSeconds());
            if (command.ExecuteNonQuery() != 1)
                return 0;
            return (ulong)command.LastInsertedId;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Roster save insert failed");
            return 0;
        }
    }

    private static List<ulong> DistinctIds(IReadOnlyList<ulong> rosterIds) =>
        rosterIds == null ? [] : [.. rosterIds.Distinct()];

    private static string Placeholders(int count) =>
        string.Join(", ", Enumerable.Range(0, count).Select(i => $"@p{i}"));
}

/// <summary>
/// Content roster removal: ownership is checked against <c>account_content_rosters</c>, then the
/// batch is deleted and a definitive result is returned. The save cooldown is not a delete gate.
/// </summary>
public sealed class ContentRosterService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly IContentRosterStore _store;
    private readonly ConcurrentDictionary<ulong, DateTime> _lastSaveAt = new();

    public const string SaveCoolTimeKey = "content_roster_save_cool_time";
    public const string MinMemberSizeKey = "content_roster_min_member_size";
    public const int MaxTitleChars = 255;

    public ContentRosterService(IContentRosterStore store)
    {
        _store = store;
    }

    public static ContentRosterService Instance { get; private set; } = new(new MySqlContentRosterStore());

    public static void SetInstanceForTest(ContentRosterService service) =>
        Instance = service ?? new ContentRosterService(new MySqlContentRosterStore());

    public ContentRosterDeleteOutcome Delete(ulong accountId, IReadOnlyList<ulong> rosterIds, DateTime now)
    {
        if (rosterIds == null || rosterIds.Count == 0)
            return new ContentRosterDeleteOutcome(ContentRosterDeleteResult.InvalidRequest, 0);

        var ids = rosterIds.Distinct().ToList();

        var existing = _store.QueryExisting(ids);
        if (ids.Any(id => !existing.Contains(id)))
        {
            Logger.Warn("Roster delete for account {0}: {1} id(s) are not in account_content_rosters.",
                accountId, ids.Count(id => !existing.Contains(id)));
            return new ContentRosterDeleteOutcome(ContentRosterDeleteResult.UnknownRoster, 0);
        }

        var owned = _store.QueryOwned(accountId, ids);
        if (ids.Any(id => !owned.Contains(id)))
        {
            Logger.Warn("Roster delete for account {0}: {1} id(s) belong to another account.",
                accountId, ids.Count(id => !owned.Contains(id)));
            return new ContentRosterDeleteOutcome(ContentRosterDeleteResult.NotOwner, 0);
        }

        var deleted = _store.DeleteOwned(accountId, ids);
        if (deleted != ids.Count)
        {
            Logger.Error("Roster delete for account {0}: removed {1} of {2} row(s).",
                accountId, deleted, ids.Count);
            return new ContentRosterDeleteOutcome(ContentRosterDeleteResult.InvalidRequest, deleted);
        }

        return new ContentRosterDeleteOutcome(ContentRosterDeleteResult.Success, deleted);
    }

    public ContentRosterSaveOutcome Save(Character character, string title, DateTime now)
    {
        if (character == null || !TitleFits(title))
            return ContentRosterSaveOutcome.Failed(ErrorMessageType.ContentRosterSaveFailed);

        var team = TeamManager.Instance.GetActiveTeamByUnit(character.Id);
        if (team == null || team.IsParty)
            return ContentRosterSaveOutcome.Failed(ErrorMessageType.ContentRosterNotFoundTeam);
        if (team.OwnerId != character.Id)
            return ContentRosterSaveOutcome.Failed(ErrorMessageType.ContentRosterNotUsableOwner);

        var minMembers = ContentConfigGameData.Instance.RequireInt(MinMemberSizeKey);
        if (team.MembersCount() < minMembers)
            return ContentRosterSaveOutcome.Failed(ErrorMessageType.ContentRosterSaveMemberSize);

        var cooldownSeconds = ContentConfigGameData.Instance.RequireInt(SaveCoolTimeKey);
        var accountId = (ulong)character.AccountId;
        if (cooldownSeconds > 0 &&
            _lastSaveAt.TryGetValue(accountId, out var lastAt) &&
            ServerCalendar.AsUtc(now) - ServerCalendar.AsUtc(lastAt) < TimeSpan.FromSeconds(cooldownSeconds))
            return ContentRosterSaveOutcome.Failed(ErrorMessageType.ContentRosterSaveCoolTime);

        var trimmed = title.Trim();
        var id = _store.Insert(accountId, trimmed, now);
        if (id == 0)
            return ContentRosterSaveOutcome.Failed(ErrorMessageType.ContentRosterSaveFailed);

        _lastSaveAt[accountId] = now;
        return new ContentRosterSaveOutcome(true, ErrorMessageType.NoErrorMessage, (long)id, now, trimmed);
    }

    private static bool TitleFits(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > MaxTitleChars)
            return false;
        foreach (var ch in title)
        {
            if (char.IsSurrogate(ch))
                return false;
        }

        return true;
    }
}

public sealed record ContentRosterSaveOutcome(bool Success, ErrorMessageType Error, long Id, DateTime RecordedAt, string Title)
{
    public static ContentRosterSaveOutcome Failed(ErrorMessageType error) =>
        new(false, error, 0, DateTime.UnixEpoch, string.Empty);
}
