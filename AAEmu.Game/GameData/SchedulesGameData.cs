using System;
using System.Collections.Generic;

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
        _gameSchedules = new Dictionary<int, GameSchedules>();
        _gameScheduleSpawners = new Dictionary<int, GameScheduleSpawners>();
        _gameScheduleDoodads = new Dictionary<int, GameScheduleDoodads>();
        _gameScheduleQuests = new Dictionary<int, GameScheduleQuests>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM game_schedules";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new GameSchedules();
                    template.Id = reader.GetInt32("id");
                    //template.Name = reader.GetString("name");
                    template.DayOfWeekId = (DayOfWeek)reader.GetInt32("day_of_week_id");
                    template.StartTime = reader.GetInt32("start_time");
                    template.EndTime = reader.GetInt32("end_time");
                    template.StYear = reader.GetInt32("st_year");
                    if (template.StYear < DateTime.UtcNow.Year)
                    {
                        template.StYear = DateTime.UtcNow.Year;
                    }
                    template.StMonth = reader.GetInt32("st_month");
                    template.StDay = reader.GetInt32("st_day");
                    template.StHour = reader.GetInt32("st_hour");
                    template.StMin = reader.GetInt32("st_min");
                    template.EdYear = reader.GetInt32("ed_year");
                    if (template.EdYear < DateTime.UtcNow.Year)
                    {
                        template.EdYear = 9999;
                    }
                    template.EdMonth = reader.GetInt32("ed_month");
                    template.EdDay = reader.GetInt32("ed_day");
                    template.EdHour = reader.GetInt32("ed_hour");
                    template.EdMin = reader.GetInt32("ed_min");
                    template.StartTimeMin = reader.GetInt32("start_time_min");
                    template.EndTimeMin = reader.GetInt32("end_time_min");
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
                    var template = new GameScheduleSpawners();
                    template.Id = reader.GetInt32("id");
                    template.GameScheduleId = reader.GetInt32("game_schedule_id");
                    template.SpawnerId = reader.GetInt32("spawner_id");

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
                    var template = new GameScheduleDoodads();
                    template.Id = reader.GetInt32("id");
                    template.GameScheduleId = reader.GetInt32("game_schedule_id");
                    template.DoodadId = reader.GetInt32("doodad_id");

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
                    var template = new GameScheduleQuests();
                    template.Id = reader.GetInt32("id");
                    template.GameScheduleId = reader.GetInt32("game_schedule_id");
                    template.QuestId = reader.GetInt32("quest_id");

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
