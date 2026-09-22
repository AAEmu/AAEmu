using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.Game.Core.Managers.World;

public sealed class MySqlConflictZoneRuntimeStore : IConflictZoneRuntimeStore
{
    public IReadOnlyDictionary<ushort, ConflictZoneRuntimeState> LoadAll()
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT zone_group_id,state,kill_count,npc_kill_count,quest_completion_count,next_state_time FROM conflict_zone_runtime_states";
        using var reader = command.ExecuteReader();
        var states = new Dictionary<ushort, ConflictZoneRuntimeState>();
        while (reader.Read())
        {
            var zoneGroupId = reader.GetUInt16(0);
            states[zoneGroupId] = new ConflictZoneRuntimeState(
                zoneGroupId,
                (ZoneConflictType)reader.GetByte(1),
                reader.GetUInt32(2),
                reader.GetUInt32(3),
                reader.GetUInt32(4),
                reader.IsDBNull(5) ? DateTime.MinValue : DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc));
        }

        return states;
    }

    public void Save(ConflictZoneRuntimeState state)
    {
        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO conflict_zone_runtime_states
                (zone_group_id,state,kill_count,npc_kill_count,quest_completion_count,next_state_time)
            VALUES (@zone,@state,@kills,@npc,@quests,@next)
            ON DUPLICATE KEY UPDATE state=VALUES(state),kill_count=VALUES(kill_count),
                npc_kill_count=VALUES(npc_kill_count),quest_completion_count=VALUES(quest_completion_count),
                next_state_time=VALUES(next_state_time)
            """;
        command.Parameters.AddWithValue("@zone", state.ZoneGroupId);
        command.Parameters.AddWithValue("@state", (byte)state.State);
        command.Parameters.AddWithValue("@kills", state.KillCount);
        command.Parameters.AddWithValue("@npc", state.NpcKillCount);
        command.Parameters.AddWithValue("@quests", state.QuestCompletionCount);
        command.Parameters.AddWithValue("@next", state.NextStateTimeUtc <= DateTime.MinValue ? DBNull.Value : state.NextStateTimeUtc);
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
