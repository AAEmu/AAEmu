using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Models.Game.Rankings;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// The ranking boards' values in MySQL: one row per holder per board per window, replaced as the holder's
/// figure moves.
/// </summary>
public sealed class MySqlRankScoreStore : IRankScoreStore
{
    private const string UpsertSql =
        "INSERT INTO character_rank_scores (rank_id,holder_kind,holder_id,period_start,account_id,world_id,value,bare_value,sub_data,updated_at) " +
        "VALUES (@rank,@kind,@holder,@period,@account,@world,@value,@bare,@sub,@updated) " +
        "ON DUPLICATE KEY UPDATE account_id=VALUES(account_id),world_id=VALUES(world_id),value=VALUES(value),bare_value=VALUES(bare_value),sub_data=VALUES(sub_data),updated_at=VALUES(updated_at)";

    public void Save(MySqlConnection connection, MySqlTransaction transaction, IReadOnlyList<RankScore> scores)
    {
        if (scores == null || scores.Count == 0)
            return;

        foreach (var score in scores)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = UpsertSql;
            command.Parameters.AddWithValue("@rank", score.RankId);
            command.Parameters.AddWithValue("@kind", (byte)score.HolderKind);
            command.Parameters.AddWithValue("@holder", score.HolderId);
            command.Parameters.AddWithValue("@period", score.PeriodStartUtc);
            command.Parameters.AddWithValue("@account", score.AccountId);
            command.Parameters.AddWithValue("@world", score.WorldId);
            command.Parameters.AddWithValue("@value", score.Value);
            command.Parameters.AddWithValue("@bare", score.BareValue);
            command.Parameters.AddWithValue("@sub", (object)score.SubData ?? DBNull.Value);
            command.Parameters.AddWithValue("@updated", score.UpdatedAtUtc);
            command.ExecuteNonQuery();
        }
    }

    public List<RankScore> ReadBoard(uint rankId, DateTime periodStartUtc, int limit)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT holder_kind,holder_id,account_id,world_id,value,bare_value,sub_data,updated_at " +
            "FROM character_rank_scores WHERE rank_id=@rank AND period_start=@period " +
            "ORDER BY value DESC, holder_id ASC LIMIT @limit";
        command.Parameters.AddWithValue("@rank", rankId);
        command.Parameters.AddWithValue("@period", periodStartUtc);
        command.Parameters.AddWithValue("@limit", limit);

        var scores = new List<RankScore>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            scores.Add(new RankScore
            {
                RankId = rankId,
                HolderKind = (RankHolderKind)reader.GetByte(0),
                HolderId = reader.GetUInt64(1),
                AccountId = reader.GetUInt32(2),
                WorldId = reader.GetByte(3),
                Value = reader.GetInt64(4),
                BareValue = reader.GetInt64(5),
                SubData = reader.IsDBNull(6) ? null : (byte[])reader.GetValue(6),
                PeriodStartUtc = periodStartUtc,
                UpdatedAtUtc = reader.GetDateTime(7)
            });
        }

        return scores;
    }

    public RankScore ReadHolder(uint rankId, DateTime periodStartUtc, RankHolderKind kind, ulong holderId)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT account_id,world_id,value,bare_value,sub_data,updated_at FROM character_rank_scores " +
            "WHERE rank_id=@rank AND period_start=@period AND holder_kind=@kind AND holder_id=@holder";
        command.Parameters.AddWithValue("@rank", rankId);
        command.Parameters.AddWithValue("@period", periodStartUtc);
        command.Parameters.AddWithValue("@kind", (byte)kind);
        command.Parameters.AddWithValue("@holder", holderId);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        return new RankScore
        {
            RankId = rankId,
            HolderKind = kind,
            HolderId = holderId,
            AccountId = reader.GetUInt32(0),
            WorldId = reader.GetByte(1),
            Value = reader.GetInt64(2),
            BareValue = reader.GetInt64(3),
            SubData = reader.IsDBNull(4) ? null : (byte[])reader.GetValue(4),
            PeriodStartUtc = periodStartUtc,
            UpdatedAtUtc = reader.GetDateTime(5)
        };
    }

    public Dictionary<ulong, long> ReadValues(uint rankId, DateTime periodStartUtc, IReadOnlyCollection<ulong> holderIds)
    {
        var values = new Dictionary<ulong, long>();
        if (holderIds == null || holderIds.Count == 0)
            return values;

        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();

        var names = new List<string>(holderIds.Count);
        var index = 0;
        foreach (var holderId in holderIds)
        {
            var name = "@holder" + index++;
            names.Add(name);
            command.Parameters.AddWithValue(name, holderId);
        }

        command.CommandText =
            $"SELECT holder_id, value FROM character_rank_scores WHERE rank_id=@rank AND period_start=@period " +
            $"AND holder_kind=@kind AND holder_id IN ({string.Join(",", names)})";
        command.Parameters.AddWithValue("@rank", rankId);
        command.Parameters.AddWithValue("@period", periodStartUtc);
        command.Parameters.AddWithValue("@kind", (byte)RankHolderKind.Character);

        using var reader = command.ExecuteReader();
        while (reader.Read())
            values[reader.GetUInt64(0)] = reader.GetInt64(1);

        return values;
    }

    public void AddGamePointTotals(MySqlConnection connection, MySqlTransaction transaction, RankScore holder,
        DateTime periodStartUtc, IReadOnlyDictionary<(int Kind, int Method), long> totals, DateTime updatedAtUtc)
    {
        if (totals == null || totals.Count == 0)
            return;

        foreach (var ((kind, method), amount) in totals)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "INSERT INTO character_game_point_totals (character_id,point_kind,point_method,period_start,account_id,world_id,total,updated_at) " +
                "VALUES (@character,@kind,@method,@period,@account,@world,@total,@updated) " +
                "ON DUPLICATE KEY UPDATE total = total + VALUES(total), account_id = VALUES(account_id), " +
                "world_id = VALUES(world_id), updated_at = VALUES(updated_at)";
            command.Parameters.AddWithValue("@character", holder.HolderId);
            command.Parameters.AddWithValue("@kind", kind);
            command.Parameters.AddWithValue("@method", method);
            command.Parameters.AddWithValue("@period", periodStartUtc);
            command.Parameters.AddWithValue("@account", holder.AccountId);
            command.Parameters.AddWithValue("@world", holder.WorldId);
            command.Parameters.AddWithValue("@total", amount);
            command.Parameters.AddWithValue("@updated", updatedAtUtc);
            command.ExecuteNonQuery();
        }
    }

    public long ReadGamePointTotal(ulong characterId, int kind, int method, DateTime periodStartUtc)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT total FROM character_game_point_totals " +
            "WHERE character_id=@character AND point_kind=@kind AND point_method=@method AND period_start=@period";
        command.Parameters.AddWithValue("@character", characterId);
        command.Parameters.AddWithValue("@kind", kind);
        command.Parameters.AddWithValue("@method", method);
        command.Parameters.AddWithValue("@period", periodStartUtc);

        var total = command.ExecuteScalar();
        return total == null || total == DBNull.Value ? 0 : Convert.ToInt64(total);
    }

    public List<RankScore> ReadGamePointBoard(uint rankId, int kind, int method, DateTime periodStartUtc)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT character_id, account_id, world_id, total, updated_at FROM character_game_point_totals " +
            "WHERE point_kind=@kind AND point_method=@method AND period_start=@period AND total > 0 " +
            "ORDER BY total DESC";
        command.Parameters.AddWithValue("@kind", kind);
        command.Parameters.AddWithValue("@method", method);
        command.Parameters.AddWithValue("@period", periodStartUtc);

        var scores = new List<RankScore>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            scores.Add(new RankScore
            {
                RankId = rankId,
                HolderKind = RankHolderKind.Character,
                HolderId = reader.GetUInt64(0),
                AccountId = reader.GetUInt32(1),
                WorldId = reader.GetByte(2),
                Value = reader.GetInt64(3),
                BareValue = 0,
                PeriodStartUtc = periodStartUtc,
                UpdatedAtUtc = reader.GetDateTime(4)
            });
        }

        return scores;
    }

    public void AddRecords(MySqlConnection connection, MySqlTransaction transaction, RankScore holder,
        DateTime periodStartUtc, IReadOnlyList<RankRecordEvent> records, DateTime updatedAtUtc)
    {
        if (records == null || records.Count == 0)
            return;

        foreach (var record in records)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;

            // The window's best keeps the figure it already had unless the new one beats it; the window's
            // total always adds. The recorded_at assignment has to come first for the best, because MySQL
            // applies the assignments left to right and it compares against the value as it still is.
            command.CommandText = RankRecordRules.AggregateOf(record.Kind) == RankRecordAggregate.Best
                ? "INSERT INTO character_rank_records (character_id,record_kind,period_start,value,recorded_at,account_id,world_id,updated_at) " +
                  "VALUES (@character,@kind,@period,@value,@recorded,@account,@world,@updated) " +
                  "ON DUPLICATE KEY UPDATE recorded_at = IF(VALUES(value) > value, VALUES(recorded_at), recorded_at), " +
                  "value = GREATEST(value, VALUES(value)), account_id = VALUES(account_id), world_id = VALUES(world_id), updated_at = VALUES(updated_at)"
                : "INSERT INTO character_rank_records (character_id,record_kind,period_start,value,recorded_at,account_id,world_id,updated_at) " +
                  "VALUES (@character,@kind,@period,@value,@recorded,@account,@world,@updated) " +
                  "ON DUPLICATE KEY UPDATE value = value + VALUES(value), recorded_at = VALUES(recorded_at), " +
                  "account_id = VALUES(account_id), world_id = VALUES(world_id), updated_at = VALUES(updated_at)";

            command.Parameters.AddWithValue("@character", holder.HolderId);
            command.Parameters.AddWithValue("@kind", (byte)record.Kind);
            command.Parameters.AddWithValue("@period", periodStartUtc);
            command.Parameters.AddWithValue("@value", record.Value);
            command.Parameters.AddWithValue("@recorded", record.RecordedAtUtc);
            command.Parameters.AddWithValue("@account", holder.AccountId);
            command.Parameters.AddWithValue("@world", holder.WorldId);
            command.Parameters.AddWithValue("@updated", updatedAtUtc);
            command.ExecuteNonQuery();
        }
    }

    public List<RankScore> ReadRecordBoard(uint rankId, RankRecordKind kind, DateTime periodStartUtc)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT character_id, account_id, world_id, value, recorded_at FROM character_rank_records " +
            "WHERE record_kind=@kind AND period_start=@period AND value > 0 " +
            "ORDER BY value DESC";
        command.Parameters.AddWithValue("@kind", (byte)kind);
        command.Parameters.AddWithValue("@period", periodStartUtc);

        var scores = new List<RankScore>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            scores.Add(new RankScore
            {
                RankId = rankId,
                HolderKind = RankHolderKind.Character,
                HolderId = reader.GetUInt64(0),
                AccountId = reader.GetUInt32(1),
                WorldId = reader.GetByte(2),
                Value = reader.GetInt64(3),

                // The window shows when the figure was recorded, which for a catch is when it was caught.
                UpdatedAtUtc = reader.GetDateTime(4),
                PeriodStartUtc = periodStartUtc
            });
        }

        return scores;
    }

    public bool HasPayout(uint rankId, DateTime periodStartUtc)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT EXISTS(SELECT 1 FROM rank_period_payouts WHERE rank_id=@rank AND period_start=@period)";
        command.Parameters.AddWithValue("@rank", rankId);
        command.Parameters.AddWithValue("@period", periodStartUtc);

        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    public void MarkPayout(uint rankId, DateTime periodStartUtc, DateTime paidAtUtc)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO rank_period_payouts (rank_id, period_start, paid_at) VALUES (@rank, @period, @paid) " +
            "ON DUPLICATE KEY UPDATE paid_at=VALUES(paid_at)";
        command.Parameters.AddWithValue("@rank", rankId);
        command.Parameters.AddWithValue("@period", periodStartUtc);
        command.Parameters.AddWithValue("@paid", paidAtUtc);
        command.ExecuteNonQuery();
    }
}
