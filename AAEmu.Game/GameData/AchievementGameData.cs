using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Achievement;
using AAEmu.Game.Models.Game.Achievement.Enums;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

[GameData]
public class AchievementGameData : Singleton<AchievementGameData>, IGameDataLoader
{
    // Empty rather than null before Load runs: a character can be built (and level up) without content, and
    // the achievement side of that must answer "nothing" rather than throw.
    private Dictionary<uint, CharRecords> _charRecords = [];
    private Dictionary<uint, Achievements> _achievements = [];
    private Dictionary<uint, List<AchievementObjectives>> _achievementObjectives = [];
    private Dictionary<uint, List<PreCompletedAchievements>> _preCompletedAchievements = [];

    /// <summary>Which achievements watch a record, so a reported record only re-evaluates its own.</summary>
    private Dictionary<uint, List<uint>> _achievementsByRecord = [];

    /// <summary>
    /// The record a <c>CompleteAchievement</c> achievement counts into, by achievement id. Completing an
    /// achievement is itself a record, which is how the achievements whose objectives are other achievements
    /// (the parent/child chains) hear about it.
    /// </summary>
    private Dictionary<uint, uint> _completionRecords = [];

    /// <summary>The records of each kind, so a reporter can find the counters it knows how to fill.</summary>
    private Dictionary<CharRecordKind, List<CharRecords>> _recordsByKind = [];

    public void Load(SqliteConnection connection)
    {
        _charRecords.Clear();
        _achievements.Clear();
        _achievementObjectives.Clear();
        _preCompletedAchievements.Clear();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM achievements";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new Achievements
                    {
                        Id = reader.GetUInt32("id"),
                        // 10.0.2.13: 'category_id' column removed
                        CompleteNum = reader.GetUInt32("complete_num", 0),
                        CompleteOr = reader.GetBoolean("complete_or"),
                        Description = reader.GetString("description", string.Empty),
                        IconId = reader.GetUInt32("icon_id", 0),
                        // 10.0.2.13: 'is_active' column removed
                        IsHidden = reader.GetBoolean("is_hidden"),
                        ItemId = reader.GetUInt32("item_id", 0),
                        Name = reader.GetString("name", string.Empty),
                        OrUnitReqs = reader.GetBoolean("or_unit_reqs"),
                        ParentAchievementId = reader.GetUInt32("parent_achievement_id", 0),
                        Priority = reader.GetUInt32("priority", 0),
                        // 10.0.2.13: 'sub_category_id' renamed to 'achievement_sub_category_id'
                        SubCategoryId = reader.GetUInt32("achievement_sub_category_id", 0),
                        Summary = reader.GetString("summary", string.Empty)
                    };

                    _achievements.TryAdd(template.Id, template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM achievement_objectives";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new AchievementObjectives
                    {
                        Id = reader.GetUInt32("id"),
                        AchievementId = reader.GetUInt32("achievement_id"),
                        OrUnitReqs = reader.GetBoolean("or_unit_reqs"),
                        RecordId = reader.GetUInt32("record_id")
                    };

                    if (!_achievementObjectives.TryGetValue(template.AchievementId, out var value))
                    {
                        value = [];
                        _achievementObjectives.Add(template.AchievementId, value);
                    }

                    value.Add(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM pre_completed_achievements";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new PreCompletedAchievements
                    {
                        Id = reader.GetUInt32("id"), CompletedAchievementId = reader.GetUInt32("completed_achievement_id"),
                        MyAchievementId = reader.GetUInt32("my_achievement_id")
                    };

                    if (!_preCompletedAchievements.TryGetValue(template.CompletedAchievementId, out var value))
                    {
                        value = [];
                        _preCompletedAchievements.Add(template.CompletedAchievementId, value);
                    }

                    value.Add(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM char_records";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new CharRecords
                    {
                        Id = reader.GetUInt32("id"),
                        KindId = (CharRecordKind)reader.GetUInt32("kind_id"),
                        Value1 = reader.GetInt32("value1"),
                        Value2 = reader.GetInt32("value2")
                    };

                    _charRecords.Add(template.Id, template);
                }
            }
        }
    }

    public void PostLoad()
    {
        _achievementsByRecord = [];
        foreach (var (achievementId, objectives) in _achievementObjectives)
        {
            foreach (var recordId in objectives.Select(objective => objective.RecordId).Distinct())
            {
                if (!_achievementsByRecord.TryGetValue(recordId, out var watching))
                {
                    watching = [];
                    _achievementsByRecord.Add(recordId, watching);
                }

                watching.Add(achievementId);
            }
        }

        _completionRecords = [];
        _recordsByKind = [];
        foreach (var record in _charRecords.Values)
        {
            if (!_recordsByKind.TryGetValue(record.KindId, out var ofKind))
            {
                ofKind = [];
                _recordsByKind.Add(record.KindId, ofKind);
            }

            ofKind.Add(record);

            if (record.KindId != CharRecordKind.CompleteAchievement || record.Value1 <= 0)
                continue;

            _completionRecords.TryAdd((uint)record.Value1, record.Id);
        }
    }

    /// <summary>Every achievement in the content, in id order.</summary>
    public IReadOnlyList<Achievements> AllAchievements =>
        _achievements.Values.OrderBy(achievement => achievement.Id).ToList();

    public Achievements GetAchievement(uint achievementId) => _achievements.GetValueOrDefault(achievementId);

    public bool HasAchievement(uint achievementId) => _achievements.ContainsKey(achievementId);

    /// <summary>An achievement's objectives, or an empty list when it has none (78 achievements have none).</summary>
    public IReadOnlyList<AchievementObjectives> GetObjectives(uint achievementId) =>
        _achievementObjectives.TryGetValue(achievementId, out var objectives) ? objectives : [];

    public CharRecords GetRecord(uint recordId) => _charRecords.GetValueOrDefault(recordId);

    /// <summary>The records of one kind — the counters something in the engine is able to report.</summary>
    public IReadOnlyList<CharRecords> GetRecordsOfKind(CharRecordKind kind) =>
        _recordsByKind.TryGetValue(kind, out var records) ? records : [];

    /// <summary>The achievements whose objectives watch a record.</summary>
    public IReadOnlyList<uint> GetAchievementsWatchingRecord(uint recordId) =>
        _achievementsByRecord.TryGetValue(recordId, out var watching) ? watching : [];

    /// <summary>The record that counts this achievement's completion, or 0 when the content has none.</summary>
    public uint GetCompletionRecord(uint achievementId) =>
        _completionRecords.GetValueOrDefault(achievementId);
}
