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
    private Dictionary<uint, ScheduleItems> _scheduleItems;

    public void Load(SqliteConnection connection, SqliteConnection connection2)
    {
        _gameSchedules = new Dictionary<int, GameSchedules>();
        _gameScheduleSpawners = new Dictionary<int, GameScheduleSpawners>();
        _gameScheduleDoodads = new Dictionary<int, GameScheduleDoodads>();
        _gameScheduleQuests = new Dictionary<int, GameScheduleQuests>();
        _scheduleItems = new Dictionary<uint, ScheduleItems>();

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
        
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM schedule_items";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new ScheduleItems();
                    template.Id = reader.GetUInt32("id");
                    template.ActiveTake = reader.GetBoolean("active_take", true);
                    template.AutoTakeDelay = reader.GetUInt32("auto_take_delay");
                    //template.disable_key_string = LocalizationManager.Instance.Get("disable_key_string", "disable_key_string", template.Id);
                    template.EdDay = reader.GetUInt32("ed_day");
                    template.EdHour = reader.GetUInt32("ed_hour");
                    template.EdMin = reader.GetUInt32("ed_min");
                    template.EdMonth = reader.GetUInt32("ed_month");
                    template.EdYear = reader.GetUInt32("ed_year");
                    //template.enable_key_string = LocalizationManager.Instance.Get("enable_key_string", "enable_key_string", template.Id);
                    template.GiveMax = reader.GetUInt32("give_max");
                    template.GiveTerm = reader.GetUInt32("give_term");
                    template.GiveTerm = reader.GetUInt32("give_term");
                    //template.icon_path = LocalizationManager.Instance.Get("icon_path", "icon_path", template.Id);
                    template.ItemCount = reader.GetUInt32("item_count");
                    template.ItemId = reader.GetUInt32("item_id");
                    template.KindId = reader.GetUInt32("kind_id");
                    template.KindValue = reader.GetUInt32("kind_value");
                    //template.label_key_string = LocalizationManager.Instance.Get("label_key_string", "label_key_string", template.Id);
                    template.MailBody = LocalizationManager.Instance.Get("mail_body", "mail_body", template.Id);
                    template.MailTitle = LocalizationManager.Instance.Get("mail_title", "mail_title", template.Id);
                    template.Name = LocalizationManager.Instance.Get("name", "name", template.Id);
                    template.OnAir = reader.GetBoolean("on_air", true);
                    template.ShowWhenever = reader.GetBoolean("show_whenever", true);
                    template.ShowWherever = reader.GetBoolean("show_wherever", true);
                    template.StDay = reader.GetUInt32("st_day");
                    template.StHour = reader.GetUInt32("st_hour");
                    template.StMin = reader.GetUInt32("st_min");
                    template.StMonth = reader.GetUInt32("st_month");
                    template.StYear = reader.GetUInt32("st_year");
                    template.StYear = reader.GetUInt32("st_year");
                    template.ToolTip = reader.GetBoolean("tool_tip", true);
                    template.WheneverTooltip = reader.GetBoolean("whenever_tooltip", true);
                    template.WhereverTooltip = reader.GetBoolean("wherever_tooltip", true);

                    _scheduleItems.TryAdd(template.Id, template);
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
        GameScheduleManager.Instance.LoadScheduleItems(_scheduleItems);
    }
}
