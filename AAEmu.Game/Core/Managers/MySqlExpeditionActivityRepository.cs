using AAEmu.Game.Models.Game.Expeditions.Activities;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public sealed class MySqlExpeditionActivityRepository(IExpeditionActivityConnectionFactory connections)
    : IExpeditionActivityRepository
{
    private const int MaximumInstanceHistoryRows = 20;
    private const int MaximumInstanceHistoryMembers = 50;
    public IReadOnlyList<ExpeditionPortalPoint> GetPortals(uint expeditionId)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, expedition_id, name, zone_id, x, y, z, z_rot FROM expedition_portals WHERE expedition_id=@expedition_id ORDER BY id";
        command.Parameters.AddWithValue("@expedition_id", expeditionId);
        using var reader = command.ExecuteReader();
        var rows = new List<ExpeditionPortalPoint>();
        while (reader.Read()) rows.Add(ReadPortal(reader));
        return rows;
    }

    public bool TryAddPortal(ExpeditionPortalPoint portal, int capacity)
    {
        if (capacity <= 0)
            return false;
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        using (var lockCommand = connection.CreateCommand())
        {
            lockCommand.Transaction = transaction;
            lockCommand.CommandText = "SELECT id FROM expeditions WHERE id=@expedition_id FOR UPDATE";
            lockCommand.Parameters.AddWithValue("@expedition_id", portal.ExpeditionId);
            if (lockCommand.ExecuteScalar() == null)
            {
                transaction.Rollback();
                return false;
            }
        }
        using (var countCommand = connection.CreateCommand())
        {
            countCommand.Transaction = transaction;
            countCommand.CommandText = "SELECT COUNT(*) FROM expedition_portals WHERE expedition_id=@expedition_id";
            countCommand.Parameters.AddWithValue("@expedition_id", portal.ExpeditionId);
            if (Convert.ToInt32(countCommand.ExecuteScalar()) >= capacity)
            {
                transaction.Rollback();
                return false;
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO expedition_portals (expedition_id,name,zone_id,x,y,z,z_rot) VALUES (@expedition_id,@name,@zone_id,@x,@y,@z,@z_rot)";
            AddPortalParameters(command, portal);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Expedition portal was not saved.");
            portal.Id = checked((uint)command.LastInsertedId);
        }
        transaction.Commit();
        return true;
    }

    public bool RenamePortal(uint expeditionId, uint portalId, string name)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE expedition_portals SET name=@name WHERE id=@id AND expedition_id=@expedition_id";
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@id", portalId);
        command.Parameters.AddWithValue("@expedition_id", expeditionId);
        return command.ExecuteNonQuery() == 1;
    }

    public bool DeletePortal(uint expeditionId, uint portalId)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM expedition_portals WHERE id=@id AND expedition_id=@expedition_id";
        command.Parameters.AddWithValue("@id", portalId);
        command.Parameters.AddWithValue("@expedition_id", expeditionId);
        return command.ExecuteNonQuery() == 1;
    }

    public IReadOnlyList<ExpeditionManagementHistory> GetManagementHistories(uint expeditionId, int limit)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT member_name, history_type, amount, used_at, detail_id, detail_value FROM expedition_management_histories WHERE expedition_id=@expedition_id ORDER BY used_at DESC,id DESC LIMIT @limit";
        command.Parameters.AddWithValue("@expedition_id", expeditionId);
        command.Parameters.AddWithValue("@limit", limit);
        using var reader = command.ExecuteReader();
        var rows = new List<ExpeditionManagementHistory>();
        while (reader.Read()) rows.Add(new(reader.GetString(0), reader.GetInt32(1), reader.GetUInt64(2),
            reader.GetDateTime(3), reader.GetUInt32(4), reader.GetInt32(5)));
        return rows;
    }

    public IReadOnlyList<ExpeditionShopHistory> GetShopHistories(uint expeditionId, int limit)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT member_name, item_id, stack, amount, purchased_at FROM expedition_shop_histories WHERE expedition_id=@expedition_id ORDER BY purchased_at DESC,id DESC LIMIT @limit";
        command.Parameters.AddWithValue("@expedition_id", expeditionId);
        command.Parameters.AddWithValue("@limit", limit);
        using var reader = command.ExecuteReader();
        var rows = new List<ExpeditionShopHistory>();
        while (reader.Read()) rows.Add(new(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2),
            reader.GetUInt64(3), reader.GetDateTime(4)));
        return rows;
    }

    public IReadOnlyList<ExpeditionWarHistory> GetWarHistories(uint expeditionId, int limit)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT declarer_id, declarer_name, defendant_id, defendant_name, declared_at, declarer_kills, defendant_kills FROM expedition_war_histories WHERE declarer_id=@expedition_id OR defendant_id=@expedition_id ORDER BY declared_at DESC,id DESC LIMIT @limit";
        command.Parameters.AddWithValue("@expedition_id", expeditionId);
        command.Parameters.AddWithValue("@limit", limit);
        using var reader = command.ExecuteReader();
        var rows = new List<ExpeditionWarHistory>();
        while (reader.Read()) rows.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2),
            reader.GetString(3), reader.GetDateTime(4), reader.GetUInt32(5), reader.GetUInt32(6)));
        return rows;
    }

    public IReadOnlyList<ExpeditionInstanceHistory> GetInstanceHistories(uint expeditionId, int limit)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT history_id,instance_rank_detail_id,instance_id,score,play_result,recorded_at FROM expedition_instance_histories WHERE expedition_id=@expedition_id ORDER BY recorded_at DESC,history_id DESC LIMIT @limit";
        command.Parameters.AddWithValue("@expedition_id", expeditionId);
        command.Parameters.AddWithValue("@limit", Math.Min(MaximumInstanceHistoryRows, limit));
        using var reader = command.ExecuteReader();
        var rows = new List<ExpeditionInstanceHistory>();
        while (reader.Read())
            rows.Add(new ExpeditionInstanceHistory
            {
                HistoryId = reader.GetUInt64(0),
                InstanceRankDetailId = reader.GetUInt32(1),
                InstanceId = reader.GetUInt32(2),
                Score = reader.GetUInt32(3),
                PlayResult = (ExpeditionInstancePlayResult)reader.GetByte(4),
                RecordedAt = reader.GetDateTime(5)
            });
        reader.Close();

        foreach (var row in rows)
        {
            using var members = connection.CreateCommand();
            members.CommandText = $"SELECT character_id,status FROM expedition_instance_history_members WHERE history_id=@history_id ORDER BY character_id LIMIT {MaximumInstanceHistoryMembers}";
            members.Parameters.AddWithValue("@history_id", row.HistoryId);
            using var memberReader = members.ExecuteReader();
            var memberRows = new List<ExpeditionInstanceHistoryMember>();
            while (memberReader.Read())
                memberRows.Add(new ExpeditionInstanceHistoryMember(row.HistoryId, memberReader.GetUInt64(0),
                    (ExpeditionInstanceMemberStatus)memberReader.GetByte(1)));
            row.Members = memberRows;
        }
        return rows;
    }

    public void AddManagementHistory(uint expeditionId, ExpeditionManagementHistory history)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        AddManagementHistory(expeditionId, history, connection, transaction);
        transaction.Commit();
    }

    public void AddManagementHistory(uint expeditionId, ExpeditionManagementHistory history,
        MySqlConnection connection, MySqlTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO expedition_management_histories (expedition_id,member_name,history_type,amount,used_at,detail_id,detail_value) VALUES (@expedition_id,@member_name,@history_type,@amount,@used_at,@detail_id,@detail_value)";
        command.Parameters.AddWithValue("@expedition_id", expeditionId);
        command.Parameters.AddWithValue("@member_name", history.MemberName);
        command.Parameters.AddWithValue("@history_type", history.Type);
        command.Parameters.AddWithValue("@amount", history.Amount);
        command.Parameters.AddWithValue("@used_at", history.UsedAt);
        command.Parameters.AddWithValue("@detail_id", history.DetailId);
        command.Parameters.AddWithValue("@detail_value", history.DetailValue);
        RequireOne(command, "management history");
    }

    public void AddShopHistory(uint expeditionId, ExpeditionShopHistory history)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        AddShopHistory(expeditionId, history, connection, transaction);
        transaction.Commit();
    }

    public void AddShopHistory(uint expeditionId, ExpeditionShopHistory history,
        MySqlConnection connection, MySqlTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO expedition_shop_histories (expedition_id,member_name,item_id,stack,amount,purchased_at) VALUES (@expedition_id,@member_name,@item_id,@stack,@amount,@purchased_at)";
        command.Parameters.AddWithValue("@expedition_id", expeditionId);
        command.Parameters.AddWithValue("@member_name", history.MemberName);
        command.Parameters.AddWithValue("@item_id", history.Type);
        command.Parameters.AddWithValue("@stack", history.Stack);
        command.Parameters.AddWithValue("@amount", history.Amount);
        command.Parameters.AddWithValue("@purchased_at", history.PurchasedAt);
        RequireOne(command, "shop history");
    }

    public void AddWarHistory(uint expeditionId, ExpeditionWarHistory history)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        AddWarHistory(history, connection, transaction);
        transaction.Commit();
    }

    public void AddWarHistory(ExpeditionWarHistory history, MySqlConnection connection,
        MySqlTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO expedition_war_histories (declarer_id,declarer_name,defendant_id,defendant_name,declared_at,declarer_kills,defendant_kills) VALUES (@declarer_id,@declarer_name,@defendant_id,@defendant_name,@declared_at,@declarer_kills,@defendant_kills)";
        command.Parameters.AddWithValue("@declarer_id", history.Type);
        command.Parameters.AddWithValue("@declarer_name", history.DeclarerName);
        command.Parameters.AddWithValue("@defendant_id", history.DefendantType);
        command.Parameters.AddWithValue("@defendant_name", history.DefendantName);
        command.Parameters.AddWithValue("@declared_at", history.DeclaredAt);
        command.Parameters.AddWithValue("@declarer_kills", history.DeclarerKills);
        command.Parameters.AddWithValue("@defendant_kills", history.DefendantKills);
        RequireOne(command, "war history");
    }

    public void AddInstanceHistory(uint expeditionId, ExpeditionInstanceHistory history)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        AddInstanceHistory(expeditionId, history, connection, transaction);
        transaction.Commit();
    }

    public void AddInstanceHistory(uint expeditionId, ExpeditionInstanceHistory history,
        MySqlConnection connection, MySqlTransaction transaction)
    {
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO expedition_instance_histories (expedition_id,instance_rank_detail_id,instance_id,score,play_result,recorded_at) VALUES (@expedition_id,@instance_rank_detail_id,@instance_id,@score,@play_result,@recorded_at)";
            command.Parameters.AddWithValue("@expedition_id", expeditionId);
            command.Parameters.AddWithValue("@instance_rank_detail_id", history.InstanceRankDetailId);
            command.Parameters.AddWithValue("@instance_id", history.InstanceId);
            command.Parameters.AddWithValue("@score", history.Score);
            command.Parameters.AddWithValue("@play_result", (byte)history.PlayResult);
            command.Parameters.AddWithValue("@recorded_at", history.RecordedAt);
            RequireOne(command, "instance history");
            history.HistoryId = checked((ulong)command.LastInsertedId);
        }
        foreach (var member in history.Members.Take(MaximumInstanceHistoryMembers))
        {
            member.HistoryId = history.HistoryId;
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO expedition_instance_history_members (history_id,character_id,status) VALUES (@history_id,@character_id,@status)";
            command.Parameters.AddWithValue("@history_id", history.HistoryId);
            command.Parameters.AddWithValue("@character_id", member.CharacterId);
            command.Parameters.AddWithValue("@status", (byte)member.Status);
            RequireOne(command, "instance history member");
        }
    }

    private static ExpeditionPortalPoint ReadPortal(MySqlDataReader reader) => new()
    {
        Id = reader.GetUInt32(0), ExpeditionId = reader.GetUInt32(1), Name = reader.GetString(2),
        ZoneId = reader.GetUInt32(3), X = reader.GetFloat(4), Y = reader.GetFloat(5),
        Z = reader.GetFloat(6), ZRot = reader.GetFloat(7)
    };

    private static void AddPortalParameters(MySqlCommand command, ExpeditionPortalPoint portal)
    {
        command.Parameters.AddWithValue("@expedition_id", portal.ExpeditionId);
        command.Parameters.AddWithValue("@name", portal.Name);
        command.Parameters.AddWithValue("@zone_id", portal.ZoneId);
        command.Parameters.AddWithValue("@x", portal.X);
        command.Parameters.AddWithValue("@y", portal.Y);
        command.Parameters.AddWithValue("@z", portal.Z);
        command.Parameters.AddWithValue("@z_rot", portal.ZRot);
    }

    private static void RequireOne(MySqlCommand command, string kind)
    {
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException($"Expedition {kind} was not saved.");
    }
}
