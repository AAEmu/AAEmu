using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

public enum FactionPowerRecordType : uint
{
    Siege = 1,
    Competition = 2,
    Quest = 3,
    Raid = 4,
    FactionChange = 5
}

public sealed record FactionPowerRecord(FactionPowerRecordType Type, uint TargetId, string TargetType, float Point, uint MemberWeightId);

public sealed record FactionPowerGrade(int Grade, int MinScore, string Tooltip);

public sealed record FactionExileRouteTemplate(uint FromFactionId, uint ToFactionId, uint NpcSpawnerId);

public sealed record FactionChangeLimit(int Over, int Less, int Count);

/// <summary>
/// The faction activity tables: what each activity is worth (<c>faction_power_records</c>), how the worth
/// scales with the number of participants (<c>member_weights</c>), the ten activity stages
/// (<c>faction_power_grades</c>), the exile brokers per route (<c>faction_change_npc_spawners</c>) and the
/// weekly exile limits (<c>faction_change_limit_nums</c>).
/// </summary>
[GameData]
public class FactionPowerGameData : Singleton<FactionPowerGameData>, IGameDataLoader
{
    /// <summary>Weight groups whose values are percentages; the others are plain multipliers.</summary>
    private static readonly HashSet<uint> PercentWeightGroups = [1, 2, 3, 6];

    private List<FactionPowerRecord> _records = [];
    private Dictionary<uint, List<(int MemberCount, int Weight)>> _memberWeights = [];
    private List<FactionPowerGrade> _grades = [];
    private List<FactionExileRouteTemplate> _routes = [];
    private List<FactionChangeLimit> _limits = [];

    public IReadOnlyList<FactionPowerGrade> Grades => _grades;
    public IReadOnlyList<FactionExileRouteTemplate> ExileRoutes => _routes;
    public IReadOnlyList<FactionChangeLimit> ChangeLimits => _limits;

    public FactionPowerRecord GetRecord(FactionPowerRecordType type, uint targetId) =>
        _records.FirstOrDefault(r => r.Type == type && r.TargetId == targetId);

    public FactionPowerRecord GetRecord(FactionPowerRecordType type) =>
        _records.FirstOrDefault(r => r.Type == type);

    public IEnumerable<FactionPowerRecord> GetRecords(FactionPowerRecordType type) => _records.Where(r => r.Type == type);

    /// <summary>Points an activity earns for this many participants.</summary>
    public double Points(FactionPowerRecord record, int memberCount) =>
        record == null ? 0 : FactionPowerWeightRules.Apply(record.Point, record.MemberWeightId,
            _memberWeights.GetValueOrDefault(record.MemberWeightId), memberCount, PercentWeightGroups.Contains(record.MemberWeightId));

    public void Load(SqliteConnection connection)
    {
        _records = [];
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM faction_power_records";
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                _records.Add(new FactionPowerRecord(
                    (FactionPowerRecordType)reader.GetUInt32("record_type_id"),
                    reader.GetUInt32("target_id", 0),
                    reader.GetString("target_type", string.Empty),
                    reader.GetFloat("point"),
                    reader.GetUInt32("member_weight_id", 0)));
            }
        }

        _memberWeights = [];
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM member_weights ORDER BY member_weight_id, member_count";
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var group = reader.GetUInt32("member_weight_id");
                if (!_memberWeights.TryGetValue(group, out var rows))
                    _memberWeights[group] = rows = [];
                rows.Add((reader.GetInt32("member_count"), reader.GetInt32("weight")));
            }
        }

        _grades = [];
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM faction_power_grades ORDER BY value";
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            var grade = 0;
            while (reader.Read())
                _grades.Add(new FactionPowerGrade(++grade, reader.GetInt32("value"), reader.GetString("tooltip", string.Empty)));
        }

        _routes = [];
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM faction_change_npc_spawners";
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                _routes.Add(new FactionExileRouteTemplate(
                    reader.GetUInt32("from_faction_id"), reader.GetUInt32("to_faction_id"), reader.GetUInt32("npc_spawner_id")));
            }
        }

        _limits = [];
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM faction_change_limit_nums ORDER BY over";
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
                _limits.Add(new FactionChangeLimit(reader.GetInt32("over"), reader.GetInt32("less"), reader.GetInt32("count")));
        }
    }

    public void PostLoad()
    {
    }
}

public static class FactionPowerWeightRules
{
    /// <summary>
    /// The weight is the row with the largest member_count not above the participant count. Percentage groups
    /// scale the points by weight/100, the others multiply them. Without a weight group the points stand.
    /// </summary>
    public static double Apply(float point, uint weightGroup, IReadOnlyList<(int MemberCount, int Weight)> rows,
        int memberCount, bool percent)
    {
        if (weightGroup == 0 || rows == null || rows.Count == 0)
            return point;

        var weight = 0;
        foreach (var (count, value) in rows)
        {
            if (count > memberCount)
                break;
            weight = value;
        }

        return percent ? point * weight / 100.0 : point * weight;
    }

    /// <summary>The weekly exile limit for a power gap in percent (0-100), from faction_change_limit_nums.</summary>
    public static int LimitFor(IReadOnlyList<FactionChangeLimit> limits, double gapPercent)
    {
        foreach (var limit in limits)
        {
            if (gapPercent > limit.Over && gapPercent <= limit.Less)
                return limit.Count;
        }

        return limits.Count > 0 ? limits[^1].Count : 0;
    }
}
