using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Models.Game.Char;

/// <summary>One achievement's progress for one character.</summary>
public sealed class AchievementProgress
{
    public uint AchievementId { get; set; }

    /// <summary>How far the character has got: a record total, or how many objectives are done.</summary>
    public int Amount { get; set; }

    /// <summary>When it was completed, or null while it is still in progress.</summary>
    public DateTime? CompletedAtUtc { get; set; }

    public bool Complete => CompletedAtUtc.HasValue;

    public AchievementProgress Clone() => new()
    {
        AchievementId = AchievementId,
        Amount = Amount,
        CompletedAtUtc = CompletedAtUtc
    };
}

/// <summary>
/// What a character has done towards the achievement list: the counter each achievement has reached and
/// whether it is complete.
/// </summary>
/// <remarks>
/// Only the progress lives here — the list itself, its objectives and its rewards are content
/// (<c>achievements</c>, <c>achievement_objectives</c>). Completion is a timestamp rather than a flag because
/// the client's packets carry the completion time, and resetting an achievement drops the row rather than
/// zeroing it so a resettable achievement looks untouched to the next evaluation.
/// </remarks>
public sealed class CharacterAchievements
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly object _sync = new();
    private readonly Dictionary<uint, AchievementProgress> _progress = [];

    public CharacterAchievements(Character owner)
    {
        Owner = owner;
    }

    public Character Owner { get; }

    public int Amount(uint achievementId)
    {
        lock (_sync)
            return _progress.TryGetValue(achievementId, out var progress) ? progress.Amount : 0;
    }

    public bool IsComplete(uint achievementId)
    {
        lock (_sync)
            return _progress.TryGetValue(achievementId, out var progress) && progress.Complete;
    }

    /// <summary>When the achievement was earned, or null while it is still in progress.</summary>
    public DateTime? CompletedAt(uint achievementId)
    {
        lock (_sync)
            return _progress.TryGetValue(achievementId, out var progress) ? progress.CompletedAtUtc : null;
    }

    /// <summary>Sets an achievement's counter, never lowering it and never touching completion.</summary>
    public int SetAmount(uint achievementId, int amount)
    {
        lock (_sync)
        {
            var progress = GetOrCreate(achievementId);
            if (amount > progress.Amount)
                progress.Amount = amount;
            return progress.Amount;
        }
    }

    /// <summary>
    /// Marks an achievement complete. Completion is one-way: a later call leaves the first timestamp alone,
    /// so the time the client shows is when it was actually earned.
    /// </summary>
    /// <returns>True when this call was the one that completed it.</returns>
    public bool Complete(uint achievementId, DateTime completedAtUtc)
    {
        lock (_sync)
        {
            var progress = GetOrCreate(achievementId);
            if (progress.Complete)
                return false;

            progress.CompletedAtUtc = completedAtUtc;
            return true;
        }
    }

    /// <summary>Drops an achievement's progress, which is what the reset path is.</summary>
    public void Reset(uint achievementId)
    {
        lock (_sync)
            _progress.Remove(achievementId);
    }

    /// <summary>A copy of the progress, for save and for the GM surface.</summary>
    public IReadOnlyList<AchievementProgress> Snapshot()
    {
        lock (_sync)
            return _progress.Values.Select(progress => progress.Clone()).ToList();
    }

    private AchievementProgress GetOrCreate(uint achievementId)
    {
        if (_progress.TryGetValue(achievementId, out var progress))
            return progress;

        progress = new AchievementProgress { AchievementId = achievementId };
        _progress[achievementId] = progress;
        return progress;
    }

    public void Load(MySqlConnection connection)
    {
        if (Owner == null)
            return;

        try
        {
            var loaded = new Dictionary<uint, AchievementProgress>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT `achievement_id`, `amount`, `completed_at` FROM character_achievements " +
                    "WHERE `owner` = @owner";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var achievementId = reader.GetUInt32("achievement_id");
                    loaded[achievementId] = new AchievementProgress
                    {
                        AchievementId = achievementId,
                        Amount = reader.GetInt32("amount"),
                        CompletedAtUtc = reader.IsDBNull(reader.GetOrdinal("completed_at"))
                            ? null
                            : reader.GetDateTime("completed_at")
                    };
                }
            }

            lock (_sync)
            {
                _progress.Clear();
                foreach (var (achievementId, progress) in loaded)
                    _progress[achievementId] = progress;
            }
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to load achievements for {0}", Owner.Name);
        }
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        if (Owner == null)
            return;

        List<AchievementProgress> snapshot;
        lock (_sync)
            snapshot = _progress.Values.Select(progress => progress.Clone()).ToList();

        try
        {
            using var delete = connection.CreateCommand();
            delete.Connection = connection;
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM character_achievements WHERE `owner` = @owner";
            delete.Parameters.AddWithValue("@owner", Owner.Id);
            delete.ExecuteNonQuery();

            foreach (var progress in snapshot)
            {
                using var insert = connection.CreateCommand();
                insert.Connection = connection;
                insert.Transaction = transaction;
                insert.CommandText =
                    "INSERT INTO character_achievements (`owner`, `achievement_id`, `amount`, `completed_at`) " +
                    "VALUES (@owner, @achievement, @amount, @completed)";
                insert.Parameters.AddWithValue("@owner", Owner.Id);
                insert.Parameters.AddWithValue("@achievement", progress.AchievementId);
                insert.Parameters.AddWithValue("@amount", progress.Amount);
                insert.Parameters.AddWithValue("@completed",
                    progress.CompletedAtUtc.HasValue ? progress.CompletedAtUtc.Value : DBNull.Value);
                insert.ExecuteNonQuery();
            }
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to save achievements for {0}", Owner.Name);
        }
    }
}
