using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Schedules;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

using DayOfWeek = AAEmu.Game.Models.Game.Schedules.DayOfWeek;

namespace AAEmu.Game.GameData;

[GameData]
public class SchedulesGameData : Singleton<SchedulesGameData>, IGameDataLoader
{
    private Dictionary<int, GameSchedules> _gameSchedules;
    private Dictionary<int, GameScheduleSpawners> _gameScheduleSpawners;
    private Dictionary<int, GameScheduleDoodads> _gameScheduleDoodads;
    private Dictionary<int, GameScheduleQuests> _gameScheduleQuests;

    public void Load(SqliteConnection connection, SqliteConnection connection2)
    {
        _gameSchedules = [];
        _gameScheduleSpawners = [];
        _gameScheduleDoodads = [];
        _gameScheduleQuests = [];

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM game_schedules";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new GameSchedules
                    {
                        Id = reader.GetInt32("id"), Name = reader.GetString("name"), DayOfWeekId = (DayOfWeek)reader.GetInt32("day_of_week_id"),
                        StartTime = reader.GetInt32("start_time"),
                        EndTime = reader.GetInt32("end_time"),
                        StYear = reader.GetInt32("st_year"),
                        StMonth = reader.GetInt32("st_month"),
                        StDay = reader.GetInt32("st_day"),
                        StHour = reader.GetInt32("st_hour"),
                        StMin = reader.GetInt32("st_min"),
                        EdYear = reader.GetInt32("ed_year"),
                        EdMonth = reader.GetInt32("ed_month"),
                        EdDay = reader.GetInt32("ed_day"),
                        EdHour = reader.GetInt32("ed_hour"),
                        EdMin = reader.GetInt32("ed_min"),
                        StartTimeMin = reader.GetInt32("start_time_min"),
                        EndTimeMin = reader.GetInt32("end_time_min")
                    };
                    _gameSchedules.TryAdd(template.Id, template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM game_schedule_spawners";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new GameScheduleSpawners
                    {
                        Id = reader.GetInt32("id"),
                        GameScheduleId = reader.GetInt32("game_schedule_id"),
                        SpawnerId = reader.GetInt32("spawner_id")
                    };

                    _gameScheduleSpawners.TryAdd(template.Id, template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM game_schedule_doodads";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new GameScheduleDoodads
                    {
                        Id = reader.GetInt32("id"),
                        GameScheduleId = reader.GetInt32("game_schedule_id"),
                        DoodadId = reader.GetInt32("doodad_id")
                    };

                    _gameScheduleDoodads.TryAdd(template.Id, template);
                }
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM game_schedule_quests";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new GameScheduleQuests
                    {
                        Id = reader.GetInt32("id"),
                        GameScheduleId = reader.GetInt32("game_schedule_id"),
                        QuestId = reader.GetInt32("quest_id")
                    };

                    _gameScheduleQuests.TryAdd(template.Id, template);
                }
            }
        }
    }

    public void PostLoad()
    {
        GameScheduleManager.Instance.LoadGameSchedules(_gameSchedules);
        GameScheduleManager.Instance.LoadGameScheduleSpawners(_gameScheduleSpawners);
        GameScheduleManager.Instance.LoadGameScheduleDoodads(_gameScheduleDoodads);
        GameScheduleManager.Instance.LoadGameScheduleQuests(_gameScheduleQuests);
    }
}
