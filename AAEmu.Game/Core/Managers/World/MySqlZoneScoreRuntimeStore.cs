using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// MySQL-backed zone-score runtime storage. The level column is written alongside the score but
/// never read back as authority: <see cref="ZoneScoreRuntime"/> re-derives the level from the score
/// through the shipped catalogs, so a stale or hand-edited level cannot survive a restart.
/// </summary>
public sealed class MySqlZoneScoreRuntimeStore : IZoneScoreRuntimeStore
{
    public IReadOnlyDictionary<uint, ZoneScoreRuntimeEntry> Load(uint zoneGroupId)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT zone_group_id,zone_score_kind_id,score,level FROM zone_score_runtime_states WHERE zone_group_id=@zone";
        command.Parameters.AddWithValue("@zone", zoneGroupId);
        using var reader = command.ExecuteReader();
        var entries = new Dictionary<uint, ZoneScoreRuntimeEntry>();
        while (reader.Read())
        {
            var entry = new ZoneScoreRuntimeEntry(
                reader.GetUInt32(1),
                reader.GetUInt32(0),
                reader.GetInt64(2),
                reader.GetInt32(3));
            entries[entry.KindId] = entry;
        }

        return entries;
    }

    public void Save(ZoneScoreRuntimeEntry entry)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO zone_score_runtime_states (zone_group_id,zone_score_kind_id,score,level)
            VALUES (@zone,@kind,@score,@level)
            ON DUPLICATE KEY UPDATE score=VALUES(score),level=VALUES(level)
            """;
        command.Parameters.AddWithValue("@zone", entry.ZoneGroupId);
        command.Parameters.AddWithValue("@kind", entry.KindId);
        command.Parameters.AddWithValue("@score", entry.Score);
        command.Parameters.AddWithValue("@level", entry.Level);
        command.ExecuteNonQuery();
    }
}
