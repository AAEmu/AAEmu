using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Achievement;
using AAEmu.Game.Models.Game.Achievement.Enums;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.GameData;

[GameData]
public class AchievementGameData : Singleton<AchievementGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, CharRecords> _charRecords;
    private Dictionary<uint, Achievements> _achievements;
    private Dictionary<uint, List<AchievementObjectives>> _achievementObjectives;
    private Dictionary<uint, List<PreCompletedAchievements>> _preCompletedAchievements;

    public void Load(SqliteConnection connection, SqliteConnection connection2)
    {
        _charRecords = new Dictionary<uint, CharRecords>();
        _achievements = new Dictionary<uint, Achievements>();
        _achievementObjectives = new Dictionary<uint, List<AchievementObjectives>>();
        _preCompletedAchievements = new Dictionary<uint, List<PreCompletedAchievements>>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM achievements";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new Achievements();
                    template.Id = reader.GetUInt32("id");
                    template.SubCategoryId = reader.GetUInt32("achievement_sub_category_id", 0);
                    template.AppellationId = reader.GetUInt32("appellation_id", 0);
                    //template.CategoryId = reader.GetUInt32("category_id", 0);
                    template.CompleteNum = reader.GetUInt32("complete_num", 0);
                    template.CompleteOr = reader.GetBoolean("complete_or");
                    template.GradeId = reader.GetUInt32("grade_id", 0);
                    template.IconId = reader.GetUInt32("icon_id", 0);
                    template.IsHidden = reader.GetBoolean("is_hidden");
                    //template.IsActive = reader.GetBoolean("is_active");
                    template.ItemNum = reader.GetUInt32("item_num");
                    template.ItemId = reader.GetUInt32("item_id", 0);
                    template.OrUnitReqs = reader.GetBoolean("or_unit_reqs");
                    template.ParentAchievementId = reader.GetUInt32("parent_achievement_id", 0);
                    template.Priority = reader.GetUInt32("priority", 0);
                    template.SeasonOff = reader.GetBoolean("season_off");

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
                    var template = new AchievementObjectives();
                    template.Id = reader.GetUInt32("id");
                    template.AchievementId = reader.GetUInt32("achievement_id");
                    template.OrUnitReqs = reader.GetBoolean("or_unit_reqs");
                    template.RecordId = reader.GetUInt32("record_id");

                    if (!_achievementObjectives.ContainsKey(template.AchievementId))
                    {
                        _achievementObjectives.Add(template.AchievementId, new List<AchievementObjectives>());
                    }
                    _achievementObjectives[template.AchievementId].Add(template);
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
                    var template = new PreCompletedAchievements();
                    template.Id = reader.GetUInt32("id");
                    template.CompletedAchievementId = reader.GetUInt32("completed_achievement_id");
                    template.MyAchievementId = reader.GetUInt32("my_achievement_id");

                    if (!_preCompletedAchievements.ContainsKey(template.CompletedAchievementId))
                    {
                        _preCompletedAchievements.Add(template.CompletedAchievementId, new List<PreCompletedAchievements>());
                    }
                    _preCompletedAchievements[template.CompletedAchievementId].Add(template);
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
                    var template = new CharRecords();
                    template.Id = reader.GetUInt32("id");
                    template.KindId = (CharRecordKind)reader.GetUInt32("kind_id");
                    template.Value1 = reader.GetUInt32("value1");
                    template.Value2 = reader.GetUInt32("value2");

                    _charRecords.Add(template.Id, template);
                }
            }
        }
    }

    public void PostLoad()
    {
    }
}
