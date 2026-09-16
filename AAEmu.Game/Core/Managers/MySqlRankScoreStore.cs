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
        "INSERT INTO character_rank_scores (rank_id,holder_kind,holder_id,period_start,account_id,world_id,value,bare_value,updated_at) " +
        "VALUES (@rank,@kind,@holder,@period,@account,@world,@value,@bare,@updated) " +
        "ON DUPLICATE KEY UPDATE account_id=VALUES(account_id),world_id=VALUES(world_id),value=VALUES(value),bare_value=VALUES(bare_value),updated_at=VALUES(updated_at)";

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
            command.Parameters.AddWithValue("@updated", score.UpdatedAtUtc);
            command.ExecuteNonQuery();
        }
    }

    public List<RankScore> ReadBoard(uint rankId, DateTime periodStartUtc, int limit)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT holder_kind,holder_id,account_id,world_id,value,bare_value,updated_at " +
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
                PeriodStartUtc = periodStartUtc,
                UpdatedAtUtc = reader.GetDateTime(6)
            });
        }

        return scores;
    }

    public RankScore ReadHolder(uint rankId, DateTime periodStartUtc, RankHolderKind kind, ulong holderId)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT account_id,world_id,value,bare_value,updated_at FROM character_rank_scores " +
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
            PeriodStartUtc = periodStartUtc,
            UpdatedAtUtc = reader.GetDateTime(4)
        };
    }

    public void AddGamePointTotals(MySqlConnection connection, MySqlTransaction transaction, ulong characterId,
        DateTime periodStartUtc, IReadOnlyDictionary<(int Kind, int Method), long> totals, DateTime updatedAtUtc)
    {
        if (totals == null || totals.Count == 0)
            return;

        foreach (var ((kind, method), amount) in totals)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "INSERT INTO character_game_point_totals (character_id,point_kind,point_method,period_start,total,updated_at) " +
                "VALUES (@character,@kind,@method,@period,@total,@updated) " +
                "ON DUPLICATE KEY UPDATE total = total + VALUES(total), updated_at = VALUES(updated_at)";
            command.Parameters.AddWithValue("@character", characterId);
            command.Parameters.AddWithValue("@kind", kind);
            command.Parameters.AddWithValue("@method", method);
            command.Parameters.AddWithValue("@period", periodStartUtc);
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
}
