using System.IO;

using AAEmu.Commons.Utils.DB;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
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
}

public sealed class MySqlContentRosterStore : IContentRosterStore
{
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

    private static List<ulong> DistinctIds(IReadOnlyList<ulong> rosterIds) =>
        rosterIds == null ? [] : [.. rosterIds.Distinct()];

    private static string Placeholders(int count) =>
        string.Join(", ", Enumerable.Range(0, count).Select(i => $"@p{i}"));
}

/// <summary>
/// Content roster removal: ownership is checked against <c>account_content_rosters</c>, then the
/// batch is deleted and a definitive result is returned. The edit cooldown comes from the shipped
/// <c>content_configs</c> row (key only in C#; value lives in the database).
/// </summary>
public sealed class ContentRosterService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>Catalog key of the only roster cool-time row shipped in 10.0.2.13 content.</summary>
    public const string SaveCoolTimeKey = "content_roster_save_cool_time";

    private readonly IContentRosterStore _store;
    private readonly Dictionary<ulong, DateTime> _lastDeleteAt = new();

    /// <summary>Seconds; 0 or less = the gated check is disabled.</summary>
    private int _cooldownSeconds;

    private bool _cooldownResolved;
    private int _cooldownMissingWarnings;

    /// <summary>How many times the missing <see cref="SaveCoolTimeKey"/> row was reported (warn-once).</summary>
    public int CooldownMissingWarnings => _cooldownMissingWarnings;

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

        var cooldownSeconds = ResolveCooldownSeconds();
        if (cooldownSeconds > 0 &&
            _lastDeleteAt.TryGetValue(accountId, out var lastAt) &&
            ServerCalendar.AsUtc(now) - ServerCalendar.AsUtc(lastAt) < TimeSpan.FromSeconds(cooldownSeconds))
        {
            return new ContentRosterDeleteOutcome(ContentRosterDeleteResult.Cooldown, 0);
        }

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

        _lastDeleteAt[accountId] = now;
        return new ContentRosterDeleteOutcome(ContentRosterDeleteResult.Success, deleted);
    }

    /// <summary>
    /// Optional-key semantics: the cooldown row is read once, a missing row is warned about once
    /// and disables the check — never a literal fallback.
    /// </summary>
    private int ResolveCooldownSeconds()
    {
        if (_cooldownResolved)
            return _cooldownSeconds;

        _cooldownResolved = true;
        if (ContentConfigGameData.Instance.TryGetInt(SaveCoolTimeKey, out var seconds))
        {
            _cooldownSeconds = seconds;
            if (seconds <= 0)
                Logger.Info("content_configs row '{0}' is {1}; content-roster delete cooldown is off.",
                    SaveCoolTimeKey, seconds);
        }
        else
        {
            _cooldownMissingWarnings++;
            _cooldownSeconds = 0;
            Logger.Warn("Required content_configs row '{0}' is missing; the content-roster delete " +
                        "cooldown is disabled until the shipped row is available.", SaveCoolTimeKey);
        }

        return _cooldownSeconds;
    }
}
