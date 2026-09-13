using System.Text.Json;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Models.Game.Expeditions.PublicAssignments;
using AAEmu.Game.Models.Game.TodayAssignment;
using AAEmu.Game.Utils.DB;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public sealed class MySqlExpeditionPublicAssignmentRepository : IExpeditionPublicAssignmentRepository
{
    public MySqlConnection Open() => MySQL.CreateConnection();

    public IReadOnlyList<ExpeditionPublicAssignmentState> LoadCurrent(DateTime periodStart)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT expedition_id,period_start,real_step,group_id,quest_context_id,status,objectives,version,completed_at,guild_rewarded,selection_generation FROM expedition_public_assignments WHERE period_start=@period";
        command.Parameters.AddWithValue("@period", periodStart);
        using var reader = command.ExecuteReader();
        var result = new List<ExpeditionPublicAssignmentState>();
        while (reader.Read())
        {
            var state = new ExpeditionPublicAssignmentState
            {
                ExpeditionId = reader.GetUInt32(0), PeriodStart = reader.GetDateTime(1),
                RealStep = reader.GetUInt32(2), GroupId = reader.GetUInt32(3), QuestContextId = reader.GetUInt32(4),
                Status = (TodayAssignmentStatus)Convert.ToSByte(reader.GetValue(5)), Version = reader.GetUInt32(7),
                CompletedAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8), GuildRewarded = reader.GetBoolean(9),
                SelectionGeneration = reader.GetUInt32(10)
            };
            var objectives = JsonSerializer.Deserialize<int[]>(reader.GetString(6)) ?? [];
            Array.Copy(objectives, state.Objectives, Math.Min(objectives.Length, state.Objectives.Length));
            result.Add(state);
        }
        reader.Close();
        foreach (var state in result)
        {
            using var contributors = connection.CreateCommand();
            contributors.CommandText = "SELECT character_id,character_name,contribution FROM expedition_public_assignment_contributors WHERE expedition_id=@id AND period_start=@period AND real_step=@step";
            contributors.Parameters.AddWithValue("@id", state.ExpeditionId);
            contributors.Parameters.AddWithValue("@period", state.PeriodStart);
            contributors.Parameters.AddWithValue("@step", state.RealStep);
            using var rows = contributors.ExecuteReader();
            while (rows.Read()) state.Contributors[rows.GetUInt32(0)] = new(rows.GetUInt32(0), rows.GetString(1), rows.GetUInt64(2));
        }
        return result;
    }

    public bool TrySave(MySqlConnection connection, MySqlTransaction transaction,
        ExpeditionPublicAssignmentState state, uint expectedVersion)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = expectedVersion == 0
            ? "INSERT IGNORE INTO expedition_public_assignments(expedition_id,period_start,real_step,group_id,quest_context_id,status,objectives,version,selection_generation,completed_at,guild_rewarded) VALUES(@id,@period,@step,@group,@quest,@status,@objectives,1,@generation,@completed,@rewarded)"
            : "UPDATE expedition_public_assignments SET group_id=@group,quest_context_id=@quest,status=@status,objectives=@objectives,version=version+1,selection_generation=@generation,completed_at=@completed,guild_rewarded=@rewarded WHERE expedition_id=@id AND period_start=@period AND real_step=@step AND version=@expected";
        command.Parameters.AddWithValue("@id", state.ExpeditionId); command.Parameters.AddWithValue("@period", state.PeriodStart);
        command.Parameters.AddWithValue("@step", state.RealStep); command.Parameters.AddWithValue("@group", state.GroupId);
        command.Parameters.AddWithValue("@quest", state.QuestContextId); command.Parameters.AddWithValue("@status", (sbyte)state.Status);
        command.Parameters.AddWithValue("@objectives", JsonSerializer.Serialize(state.Objectives));
        command.Parameters.AddWithValue("@completed", (object)state.CompletedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("@rewarded", state.GuildRewarded); command.Parameters.AddWithValue("@expected", expectedVersion);
        command.Parameters.AddWithValue("@generation", state.SelectionGeneration);
        return command.ExecuteNonQuery() == 1;
    }

    public void UpsertContributor(MySqlConnection connection, MySqlTransaction transaction,
        ExpeditionPublicAssignmentState state, uint characterId, string characterName, int delta)
    {
        if (delta <= 0) return;
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO expedition_public_assignment_contributors(expedition_id,period_start,real_step,character_id,character_name,contribution) VALUES(@id,@period,@step,@character,@name,@delta) ON DUPLICATE KEY UPDATE character_name=VALUES(character_name),contribution=contribution+VALUES(contribution)";
        command.Parameters.AddWithValue("@id", state.ExpeditionId); command.Parameters.AddWithValue("@period", state.PeriodStart);
        command.Parameters.AddWithValue("@step", state.RealStep); command.Parameters.AddWithValue("@character", characterId);
        command.Parameters.AddWithValue("@name", characterName); command.Parameters.AddWithValue("@delta", delta);
        if (command.ExecuteNonQuery() < 1) throw new InvalidOperationException("Public-assignment contributor was not saved.");
    }
}
