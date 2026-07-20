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
    private Dictionary<uint, CharRecords> _charRecords;
    private Dictionary<uint, Achievements> _achievements;
    private Dictionary<uint, List<AchievementObjectives>> _achievementObjectives;
    private Dictionary<uint, List<PreCompletedAchievements>> _preCompletedAchievements;

    public void Load(SqliteConnection connection, SqliteConnection connection2)
    {
        _charRecords = [];
        _achievements = [];
        _achievementObjectives = [];
        _preCompletedAchievements = [];

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
                        CategoryId = reader.GetUInt32("category_id", 0),
                        CompleteNum = reader.GetUInt32("complete_num", 0),
                        CompleteOr = reader.GetBoolean("complete_or"),
                        IconId = reader.GetUInt32("icon_id", 0),
                        IsActive = reader.GetBoolean("is_active"),
                        IsHidden = reader.GetBoolean("is_hidden"),
                        ItemId = reader.GetUInt32("item_id", 0),
                        OrUnitReqs = reader.GetBoolean("or_unit_reqs"),
                        ParentAchievementId = reader.GetUInt32("parent_achievement_id", 0),
                        Priority = reader.GetUInt32("priority", 0),
                        SubCategoryId = reader.GetUInt32("sub_category_id", 0)
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
                        Value1 = reader.GetUInt32("value1"),
                        Value2 = reader.GetUInt32("value2")
                    };

                    _charRecords.Add(template.Id, template);
                }
            }
        }
    }

    public void PostLoad()
    {
    }
}
