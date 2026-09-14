using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Models.Game.Expeditions.Recruitment;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.StaticValues;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public sealed class MySqlExpeditionRecruitmentRepository(IExpeditionRecruitmentConnectionFactory connections) : IExpeditionRecruitmentRepository
{
    public IReadOnlyList<ExpeditionRecruitment> GetActive(DateTime now)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT expedition_id, interest_mask, introduction, registered_at, expires_at FROM expedition_recruitments WHERE expires_at > @now ORDER BY registered_at DESC, expedition_id";
        command.Parameters.AddWithValue("@now", now);
        using var reader = command.ExecuteReader();
        var result = new List<ExpeditionRecruitment>();
        while (reader.Read()) result.Add(ReadRecruitment(reader));
        return result;
    }

    public IReadOnlyList<ExpeditionRecruitmentApplication> GetApplicationsForExpedition(uint expeditionId, DateTime now) =>
        GetApplications("expedition_id", expeditionId, now);

    public IReadOnlyList<ExpeditionRecruitmentApplication> GetApplicationsForCharacter(uint characterId, DateTime now) =>
        GetApplications("character_id", characterId, now);

    private IReadOnlyList<ExpeditionRecruitmentApplication> GetApplications(string column, uint id, DateTime now)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT a.expedition_id, a.character_id, a.memo, a.registered_at FROM expedition_recruitment_applications a INNER JOIN expedition_recruitments r ON r.expedition_id=a.expedition_id WHERE a.{column} = @id AND r.expires_at > @now ORDER BY a.registered_at, a.expedition_id, a.character_id";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@now", now);
        using var reader = command.ExecuteReader();
        var result = new List<ExpeditionRecruitmentApplication>();
        while (reader.Read()) result.Add(new(reader.GetUInt32(0), reader.GetUInt32(1), reader.GetString(2), reader.GetDateTime(3)));
        return result;
    }

    public ExpeditionRecruitment GetForUpdate(uint expeditionId, MySqlConnection connection, MySqlTransaction transaction)
    {
        using var command = Create(connection, transaction, "SELECT expedition_id, interest_mask, introduction, registered_at, expires_at FROM expedition_recruitments WHERE expedition_id=@id FOR UPDATE");
        command.Parameters.AddWithValue("@id", expeditionId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadRecruitment(reader) : null;
    }

    public ExpeditionJoinCandidate GetCandidateForUpdate(uint characterId, MySqlConnection connection,
        MySqlTransaction transaction)
    {
        using var command = Create(connection, transaction, "SELECT id, account_id, name, level, heir_exp, faction_id, ability1, ability2, ability3, expedition_id, expedition_rejoin_until FROM characters WHERE id=@id AND deleted=0 FOR UPDATE");
        command.Parameters.AddWithValue("@id", characterId);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new(reader.GetUInt32(0), reader.GetUInt32(1), reader.GetString(2), reader.GetByte(3),
                HeirGameData.Instance.GetLevelForExp(reader.GetInt64(4)), (FactionsEnum)reader.GetUInt32(5), reader.GetByte(6), reader.GetByte(7),
                reader.GetByte(8), reader.GetInt32(9), reader.GetInt64(10))
            : null;
    }

    public ExpeditionJoinCandidate GetCandidate(uint characterId)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        var candidate = GetCandidateForUpdate(characterId, connection, transaction);
        transaction.Commit();
        return candidate;
    }

    public IReadOnlyDictionary<uint, ExpeditionJoinCandidate> GetCandidates(IReadOnlyCollection<uint> characterIds)
    {
        if (characterIds.Count == 0) return new Dictionary<uint, ExpeditionJoinCandidate>();
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        var names = characterIds.Select((_, index) => $"@id{index}").ToArray();
        command.CommandText = $"SELECT id, account_id, name, level, heir_exp, faction_id, ability1, ability2, ability3, expedition_id, expedition_rejoin_until FROM characters WHERE deleted=0 AND id IN ({string.Join(',', names)})";
        var index = 0;
        foreach (var id in characterIds) command.Parameters.AddWithValue(names[index++], id);
        using var reader = command.ExecuteReader();
        var result = new Dictionary<uint, ExpeditionJoinCandidate>();
        while (reader.Read())
        {
            var candidate = new ExpeditionJoinCandidate(reader.GetUInt32(0), reader.GetUInt32(1), reader.GetString(2),
                reader.GetByte(3), HeirGameData.Instance.GetLevelForExp(reader.GetInt64(4)),
                (FactionsEnum)reader.GetUInt32(5), reader.GetByte(6), reader.GetByte(7), reader.GetByte(8), reader.GetInt32(9),
                reader.GetInt64(10));
            result[candidate.CharacterId] = candidate;
        }
        return result;
    }

    public int CountApplicationsForUpdate(uint characterId, DateTime now, MySqlConnection connection, MySqlTransaction transaction)
    {
        // The caller already holds the candidate row lock, which serializes this character's applications.
        // Avoid locking every joined recruitment parent in guild-dependent order.
        using var command = Create(connection, transaction, "SELECT a.expedition_id FROM expedition_recruitment_applications a INNER JOIN expedition_recruitments r ON r.expedition_id=a.expedition_id WHERE a.character_id=@id AND r.expires_at > @now");
        command.Parameters.AddWithValue("@id", characterId);
        command.Parameters.AddWithValue("@now", now);
        using var reader = command.ExecuteReader();
        var count = 0;
        while (reader.Read()) count++;
        return count;
    }

    public bool ApplicationExistsForUpdate(uint expeditionId, uint characterId, MySqlConnection connection, MySqlTransaction transaction)
    {
        using var command = Create(connection, transaction, "SELECT 1 FROM expedition_recruitment_applications WHERE expedition_id=@eid AND character_id=@cid FOR UPDATE");
        command.Parameters.AddWithValue("@eid", expeditionId);
        command.Parameters.AddWithValue("@cid", characterId);
        return command.ExecuteScalar() != null;
    }

    public void Upsert(ExpeditionRecruitment value, MySqlConnection connection, MySqlTransaction transaction)
    {
        using var command = Create(connection, transaction, "INSERT INTO expedition_recruitments (expedition_id,interest_mask,introduction,registered_at,expires_at) VALUES (@id,@mask,@intro,@registered,@expires) ON DUPLICATE KEY UPDATE interest_mask=VALUES(interest_mask),introduction=VALUES(introduction),registered_at=VALUES(registered_at),expires_at=VALUES(expires_at)");
        command.Parameters.AddWithValue("@id", value.ExpeditionId);
        command.Parameters.AddWithValue("@mask", value.InterestMask);
        command.Parameters.AddWithValue("@intro", value.Introduction);
        command.Parameters.AddWithValue("@registered", value.RegisteredAt);
        command.Parameters.AddWithValue("@expires", value.ExpiresAt);
        if (command.ExecuteNonQuery() is not (1 or 2)) throw new InvalidOperationException("Recruitment was not saved.");
    }

    public bool TryDebitMoney(uint characterId, long amount, MySqlConnection connection, MySqlTransaction transaction)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        using var command = Create(connection, transaction,
            "UPDATE characters SET money=money-@amount WHERE id=@id AND deleted=0 AND money>=@amount");
        command.Parameters.AddWithValue("@id", characterId);
        command.Parameters.AddWithValue("@amount", amount);
        return command.ExecuteNonQuery() == 1;
    }

    public bool DeleteRecruitment(uint expeditionId, MySqlConnection connection, MySqlTransaction transaction) =>
        Delete(connection, transaction, "DELETE FROM expedition_recruitments WHERE expedition_id=@eid", expeditionId, 0) == 1;

    public void AddApplication(ExpeditionRecruitmentApplication value, MySqlConnection connection, MySqlTransaction transaction)
    {
        using var command = Create(connection, transaction, "INSERT INTO expedition_recruitment_applications (expedition_id,character_id,memo,registered_at) VALUES (@eid,@cid,@memo,@registered)");
        command.Parameters.AddWithValue("@eid", value.ExpeditionId);
        command.Parameters.AddWithValue("@cid", value.CharacterId);
        command.Parameters.AddWithValue("@memo", value.Memo);
        command.Parameters.AddWithValue("@registered", value.RegisteredAt);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Application was not saved.");
    }

    public bool DeleteApplication(uint expeditionId, uint characterId, MySqlConnection connection, MySqlTransaction transaction) =>
        Delete(connection, transaction, "DELETE FROM expedition_recruitment_applications WHERE expedition_id=@eid AND character_id=@cid", expeditionId, characterId) == 1;

    public int DeleteApplicationsForCharacter(uint characterId, MySqlConnection connection, MySqlTransaction transaction) =>
        Delete(connection, transaction, "DELETE FROM expedition_recruitment_applications WHERE character_id=@cid", 0, characterId);

    private static int Delete(MySqlConnection connection, MySqlTransaction transaction, string sql, uint expeditionId, uint characterId)
    {
        using var command = Create(connection, transaction, sql);
        if (expeditionId != 0) command.Parameters.AddWithValue("@eid", expeditionId);
        if (characterId != 0) command.Parameters.AddWithValue("@cid", characterId);
        return command.ExecuteNonQuery();
    }

    private static MySqlCommand Create(MySqlConnection connection, MySqlTransaction transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static ExpeditionRecruitment ReadRecruitment(MySqlDataReader reader) =>
        new(reader.GetUInt32(0), reader.GetInt16(1), reader.GetString(2), reader.GetDateTime(3), reader.GetDateTime(4));
}
