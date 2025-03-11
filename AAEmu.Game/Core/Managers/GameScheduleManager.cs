using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Schedules;

using NCrontab;

using NLog;

using static System.String;

using DayOfWeek = AAEmu.Game.Models.Game.Schedules.DayOfWeek;

namespace AAEmu.Game.Core.Managers;

public class GameScheduleManager : Singleton<GameScheduleManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private bool _loaded = false;
    private Dictionary<int, GameSchedules> _gameSchedules; // GameScheduleId, GameSchedules
    private Dictionary<int, GameScheduleSpawners> _gameScheduleSpawners;
    private Dictionary<int, List<int>> _gameScheduleSpawnerIds;
    private Dictionary<int, GameScheduleDoodads> _gameScheduleDoodads;
    private Dictionary<int, List<int>> _gameScheduleDoodadIds;
    private Dictionary<int, GameScheduleQuests> _gameScheduleQuests;
    private Dictionary<uint, ScheduleItems> _scheduleItems;
    private List<int> GameScheduleId { get; set; }

    public void Load()
    {
        if (_loaded)
            return;

        Logger.Info("Loading schedules...");

        SchedulesGameData.Instance.PostLoad();

        LoadGameScheduleSpawnersData(); // добавил разделение spawnerId для Npc & Doodads

        Logger.Info("Loaded schedules");

        _loaded = true;
    }

    public void LoadGameSchedules(Dictionary<int, GameSchedules> gameSchedules)
    {
        //_gameSchedules = new Dictionary<int, GameSchedules>();
        //foreach (var gs in gameSchedules)
        //{
        //    _gameSchedules.TryAdd(gs.Key, gs.Value);
        //}
        _gameSchedules = gameSchedules;
    }

    public void LoadGameScheduleSpawners(Dictionary<int, GameScheduleSpawners> gameScheduleSpawners)
    {
        _gameScheduleSpawners = gameScheduleSpawners;
    }

    public void LoadGameScheduleDoodads(Dictionary<int, GameScheduleDoodads> gameScheduleDoodads)
    {
        _gameScheduleDoodads = gameScheduleDoodads;
    }

    public void LoadGameScheduleQuests(Dictionary<int, GameScheduleQuests> gameScheduleQuests)
    {
        _gameScheduleQuests = gameScheduleQuests;
    }

    public void LoadScheduleItems(Dictionary<uint, ScheduleItems> scheduleItems)
    {
        _scheduleItems = scheduleItems;
    }

    public bool CheckSpawnerInScheduleSpawners(int spawnerId)
    {
        return _gameScheduleSpawnerIds.ContainsKey(spawnerId);
    }

    public bool CheckDoodadInScheduleSpawners(int doodadId)
    {
        return _gameScheduleDoodadIds.ContainsKey(doodadId);
    }

    //public bool CheckDoodadInScheduleSpawners(int spawnerId)
    //{
    //    return _gameScheduleDoodads.ContainsKey(spawnerId);
    //}

    public bool CheckSpawnerInGameSchedules(int spawnerId)
    {
        var res = CheckSpawnerScheduler(spawnerId);
        return res;
    }

    public bool CheckDoodadInGameSchedules(uint doodadId)
    {
        var res = CheckDoodadScheduler((int)doodadId);
        return res;
    }

    public bool CheckQuestInGameSchedules(uint questId)
    {
        if (!GetGameScheduleQuestsData(questId)) { return false; }
        var res = CheckScheduler();
        return res.Contains(true);
    }

    private bool CheckSpawnerScheduler(int spawnerId)
    {
        var res = false;
        foreach (var gameScheduleId in _gameScheduleSpawnerIds[spawnerId])
        {
            if (_gameSchedules.TryGetValue(gameScheduleId, out var gs))
            {
                res = true;
            }
        }

        return res;
    }

    private bool CheckDoodadScheduler(int doodadId)
    {
        var res = false;
        foreach (var gameScheduleId in _gameScheduleDoodadIds[doodadId])
        {
            if (_gameSchedules.TryGetValue(gameScheduleId, out var gs))
            {
                res = true;
            }
        }

        return res;
    }

    //private List<bool> CheckDoodadScheduler(int doodadId)
    //{
    //    if (!_gameScheduleDoodadIds.ContainsKey(doodadId))
    //    {
    //        return new List<bool>();
    //    }

    //    var res = new List<bool>();
    //    foreach (var gameScheduleId in _gameScheduleDoodadIds[doodadId])
    //    {
    //        if (_gameSchedules.TryGetValue(gameScheduleId, out var gs))
    //        {
    //            res.Add(CheckData(gs));
    //        }
    //    }

    //    return res;
    //}

    public bool PeriodHasAlreadyBegunDoodad(int doodadId)
    {
        var res = new List<bool>();
        foreach (var gameScheduleId in _gameScheduleDoodadIds[doodadId])
        {
            if (_gameSchedules.TryGetValue(gameScheduleId, out var gs))
            {
                res.Add(CheckData(gs));
            }
        }

        return res.Contains(true);
    }

    public bool PeriodHasAlreadyBegunNpc(int spawnerId)
    {
        var res = new List<bool>();
        foreach (var gameScheduleId in _gameScheduleSpawnerIds[spawnerId])
        {
            if (_gameSchedules.TryGetValue(gameScheduleId, out var gs))
            {
                res.Add(CheckData(gs));
            }
        }

        return res.Contains(true);
    }

    private List<bool> CheckScheduler()
    {
        var res = new List<bool>();
        foreach (var gameScheduleId in GameScheduleId)
        {
            if (_gameSchedules.TryGetValue(gameScheduleId, out var gs))
            {
                res.Add(CheckData(gs));
            }
        }

        return res;
    }

    public ScheduleItems GetScheduleItem(uint id)
    {
        _scheduleItems.TryGetValue(id, out var value);
        return value;
    }

    public string GetCronRemainingTime(int spawnerId, bool start = true)
    {
        var cronExpression = Empty;
        if (!_gameScheduleSpawnerIds.ContainsKey(spawnerId))
        {
            return cronExpression;
        }

        foreach (var gameScheduleId in _gameScheduleSpawnerIds[spawnerId])
        {
            if (!_gameSchedules.ContainsKey(gameScheduleId)) { continue; }

            var gameSchedules = _gameSchedules[gameScheduleId];

            cronExpression = start ? GetCronExpression(gameSchedules, true) : GetCronExpression(gameSchedules, false);
        }

        return cronExpression;
    }

    public string GetDoodadCronRemainingTime(int doodadId, bool start = true)
    {
        var cronExpression = Empty;
        if (!_gameScheduleDoodadIds.ContainsKey(doodadId))
        {
            return cronExpression;
        }

        foreach (var gameScheduleId in _gameScheduleDoodadIds[doodadId])
        {
            if (!_gameSchedules.ContainsKey(gameScheduleId)) { continue; }

            var gameSchedules = _gameSchedules[gameScheduleId];

            cronExpression = start ? GetCronExpression(gameSchedules, true) : GetCronExpression(gameSchedules, false);
        }

        return cronExpression;
    }

    public TimeSpan GetRemainingTime(int spawnerId, bool start = true)
    {
        if (!_gameScheduleSpawnerIds.ContainsKey(spawnerId))
        {
            return TimeSpan.Zero;
        }

        var remainingTime = TimeSpan.MaxValue;

        foreach (var gameScheduleId in _gameScheduleSpawnerIds[spawnerId])
        {
            if (!_gameSchedules.ContainsKey(gameScheduleId)) { continue; }

            var gameSchedules = _gameSchedules[gameScheduleId];
            var timeSpan = start ? GetRemainingTimeStart(gameSchedules) : GetRemainingTimeEnd(gameSchedules);
            if (timeSpan <= remainingTime)
            {
                remainingTime = timeSpan;
            }
        }

        return remainingTime;
    }

    public bool HasGameScheduleSpawnersData(uint spawnerTemplateId)
    {
        return _gameScheduleSpawners.Values.Any(gss => gss.SpawnerId == spawnerTemplateId);
    }

    private void LoadGameScheduleSpawnersData()
    {
        // Spawners
        _gameScheduleSpawnerIds = new Dictionary<int, List<int>>();
        foreach (var gss in _gameScheduleSpawners.Values)
        {
            if (!_gameScheduleSpawnerIds.ContainsKey(gss.SpawnerId))
            {
                _gameScheduleSpawnerIds.Add(gss.SpawnerId, new List<int> { gss.GameScheduleId });
            }
            else
            {
                _gameScheduleSpawnerIds[gss.SpawnerId].Add(gss.GameScheduleId);
            }
        }

        // Doodads
        _gameScheduleDoodadIds = new Dictionary<int, List<int>>();
        foreach (var gsd in _gameScheduleDoodads.Values)
        {
            if (!_gameScheduleDoodadIds.ContainsKey(gsd.DoodadId))
            {
                _gameScheduleDoodadIds.Add(gsd.DoodadId, new List<int> { gsd.GameScheduleId });
            }
            else
            {
                _gameScheduleDoodadIds[gsd.DoodadId].Add(gsd.GameScheduleId);
            }
        }
        //TODO: quests data
    }

    public bool GetGameScheduleDoodadsData(uint doodadId)
    {
        GameScheduleId = new List<int>();
        foreach (var gsd in _gameScheduleDoodads.Values)
        {
            if (gsd.DoodadId != doodadId) { continue; }
            GameScheduleId.Add(gsd.GameScheduleId);
        }
        return GameScheduleId.Count != 0;
    }

    public bool GetGameScheduleQuestsData(uint questId)
    {
        GameScheduleId = new List<int>();
        foreach (var gsq in _gameScheduleQuests.Values)
        {
            if (gsq.QuestId != questId) { continue; }
            GameScheduleId.Add(gsq.GameScheduleId);
        }
        return GameScheduleId.Count != 0;
    }

    private static bool CheckData(GameSchedules value)
    {
        var curHours = DateTime.UtcNow.TimeOfDay.Hours;
        var curMinutes = DateTime.UtcNow.TimeOfDay.Minutes;
        var curDay = DateTime.UtcNow.Day;
        var curMonth = DateTime.UtcNow.Month;
        var curYear = DateTime.UtcNow.Year;
        var curDayOfWeek = (DayOfWeek)DateTime.UtcNow.DayOfWeek + 1;

        if (value.DayOfWeekId == DayOfWeek.Invalid)
        {
            if (value.EndTime == 0 && value.StMonth == 0 && value.StDay == 0 && value.StHour == 0)
            {
                return true;
            }
            if (value.EndTime == 0 && curMonth >= value.StMonth && curDay >= value.StDay && curHours >= value.StHour && curMonth <= value.EdMonth && curDay <= value.EdDay && curHours <= value.EdHour)
            {
                return true;
            }
            if (curHours >= value.StartTime && curHours <= value.EndTime && curMinutes <= value.EndTimeMin && value.StMonth == 0 && value.StDay == 0 && value.StHour == 0)
            {
                return true;
            }
            if (curHours >= value.StartTime && curHours <= value.EndTime && curMinutes <= value.EndTimeMin && curMonth >= value.StMonth && curDay >= value.StDay && curMonth <= value.EdMonth && curDay <= value.EdDay)
            {
                return true;
            }
        }
        else
        {
            if (curDayOfWeek == value.DayOfWeekId)
            {
                if (value.EndTime == 0 && value.StMonth == 0 && value.StDay == 0 && value.StHour == 0)
                {
                    return true;
                }
                if (value.EndTime == 0 && curMonth >= value.StMonth && curDay >= value.StDay && curHours >= value.StHour && curMonth <= value.EdMonth && curDay <= value.EdDay && curHours <= value.EdHour)
                {
                    return true;
                }
                if (curHours >= value.StartTime && curHours <= value.EndTime && curMinutes <= value.EndTimeMin && value.StMonth == 0 && value.StDay == 0 && value.StHour == 0)
                {
                    return true;
                }
                if (curHours >= value.StartTime && curHours <= value.EndTime && curMinutes <= value.EndTimeMin && curMonth >= value.StMonth && curDay >= value.StDay && curMonth <= value.EdMonth && curDay <= value.EdDay)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static TimeSpan GetRemainingTimeStart(GameSchedules value)
    {
        var cronExpression = GetCronExpression(value, true);
        var schedule = CrontabSchedule.Parse(cronExpression, TaskManager.s_crontabScheduleParseOptions);
        return schedule.GetNextOccurrence(DateTime.UtcNow) - DateTime.UtcNow;
    }

    private static TimeSpan GetRemainingTimeEnd(GameSchedules value)
    {
        var cronExpression = GetCronExpression(value, false);
        var schedule = CrontabSchedule.Parse(cronExpression, TaskManager.s_crontabScheduleParseOptions);
        return schedule.GetNextOccurrence(DateTime.UtcNow) - DateTime.UtcNow;
    }

    private static string GetCronExpression(GameSchedules value, bool start = true)
    {
        /*
           1. Seconds / Секунды
           2. Minutes / Минуты
           3. Hours / Часы
           4. Day of the month / День месяца
           5. Month / Месяц
           6. Day of the week / День недели
           7. Year (optional field) / Год (необязательное поле) // Not supported by Crontab
        */

        var dayOfWeek = value.DayOfWeekId switch
        {
            DayOfWeek.Sunday => 0,
            DayOfWeek.Monday => 1,
            DayOfWeek.Tuesday => 2,
            DayOfWeek.Wednesday => 3,
            DayOfWeek.Thursday => 4,
            DayOfWeek.Friday => 5,
            DayOfWeek.Saturday => 6,
            _ => 8
        };

        //var stYear = value.StYear;
        var stMonth = value.StMonth;
        var stDay = value.StDay;
        var stHour = value.StHour;
        var stMinute = value.StMin;
        var startTime = value.StartTime;
        var startTimeMin = value.StartTimeMin;

        //var edYear = value.EdYear;
        var edMonth = value.EdMonth;
        var edDay = value.EdDay;
        var edHour = value.EdHour;
        var edMinute = value.EdMin;
        var endTime = value.EndTime;
        var endTimeMin = value.EndTimeMin;

        var cronExpression = Empty;

        if (start)
        {
            if (value.DayOfWeekId == DayOfWeek.Invalid)
            {
                if (value.StartTime == 0 && value.EndTime == 0 && value.StMonth == 0 && value.StDay == 0 && value.StHour == 0)
                {
                    cronExpression = "0 0 0 ? * *";  // *"; // verified
                }
                if (value.EndTime > 0 && value.StMonth == 0 && value.StDay == 0 && value.StHour == 0)
                {
                    cronExpression = $"0 {startTimeMin} {startTime} ? * *"; // *"; // not verified
                }
                if (value.EndTime > 0 && value.StMonth > 0 && value.StDay > 0)
                {
                    cronExpression = $"0 {startTimeMin} {startTime} {stDay} {stMonth} ?"; // *"; // not verified
                }
                if (value.StartTime == 0 && value.EndTime == 0 && value.StMonth == 0 && value.StDay == 0)
                {
                    cronExpression = $"0 {stMinute} {stHour} ? * *"; // *"; // verified
                }
                if (value.StartTime == 0 && value.EndTime == 0 && value.StMonth > 0 && value.StDay > 0)
                {
                    cronExpression = $"0 {stMinute} {stHour} {stDay} {stMonth} ?"; // *"; // verified
                }
                //cronExpression = $"0 {stMinute} {stHour} {stDay} {stMonth} ?"; // *";
            }
            else
            {
                if (value.StartTime == 0 && value.EndTime == 0 && value.StMonth == 0 && value.StDay == 0 && value.StHour == 0)
                {
                    cronExpression = $"0 0 0 ? * {dayOfWeek}"; // *"; // not verified
                }
                if (value.EndTime > 0 && value.StMonth == 0 && value.StDay == 0 && value.StHour == 0)
                {
                    cronExpression = $"0 {startTimeMin} {startTime} ? * {dayOfWeek}"; // *"; // verified
                }
                if (value.EndTime > 0 && value.StMonth > 0 && value.StDay > 0)
                {
                    cronExpression = $"0 {startTimeMin} {startTime} {stDay} {stMonth} {dayOfWeek}"; // *"; // not verified
                }
                if (value.StartTime == 0 && value.EndTime == 0 && value.StMonth == 0 && value.StDay == 0)
                {
                    cronExpression = $"0 {stMinute} {stHour} ? * {dayOfWeek}"; // *"; // not verified
                }
                if (value.StartTime == 0 && value.EndTime == 0 && value.StMonth > 0 && value.StDay > 0)
                {
                    cronExpression = $"0 {stMinute} {stHour} {edDay} {edMonth} {dayOfWeek}"; // *"; // not verified
                }
                //cronExpression = $"0 {stMinute} {stHour} {stDay} {stMonth} {dayOfWeek}";
            }
        }
        else
        {
            if (value.DayOfWeekId == DayOfWeek.Invalid)
            {
                if (value.StartTime == 0 && value.EndTime == 0 && value.EdMonth == 0 && value.EdDay == 0 && value.EdHour == 0)
                {
                    cronExpression = "0 0 0 ? * *"; // *"; // not verified
                }
                if (value.EndTime > 0 && value.EdMonth == 0 && value.EdDay == 0 && value.EdHour == 0)
                {
                    cronExpression = $"0 {endTimeMin} {endTime} ? * *"; // *"; // not verified
                }
                if (value.EndTime > 0 && value.EdMonth > 0 && value.EdDay > 0)
                {
                    cronExpression = $"0 {endTimeMin} {endTime} {edDay} {edMonth} ?"; // *"; // not verified
                }
                if (value.StartTime == 0 && value.EndTime == 0 && value.EdMonth == 0 && value.EdDay == 0)
                {
                    cronExpression = $"0 {edMinute} {edHour} ? * *"; // *"; // not verified
                }
                if (value.StartTime == 0 && value.EndTime == 0 && value.EdMonth > 0 && value.EdDay > 0)
                {
                    cronExpression = $"0 {edMinute} {edHour} {edDay} {edMonth} ?"; // *"; // not verified
                }
            }
            else
            {
                if (value.StartTime == 0 && value.EndTime == 0 && value.EdMonth == 0 && value.EdDay == 0 && value.EdHour == 0)
                {
                    cronExpression = $"0 0 0 ? * {dayOfWeek}"; // *"; // not verified
                }
                if (value.EndTime > 0 && value.EdMonth == 0 && value.EdDay == 0 && value.EdHour == 0)
                {
                    cronExpression = $"0 {endTimeMin} {endTime} ? * {dayOfWeek}"; // *"; // not verified
                }
                if (value.EndTime > 0 && value.EdMonth > 0 && value.EdDay > 0)
                {
                    cronExpression = $"0 {endTimeMin} {endTime} {edDay} {edMonth} ?"; // *"; // not verified
                }
                if (value.StartTime == 0 && value.EndTime == 0 && value.EdMonth == 0 && value.EdDay == 0)
                {
                    cronExpression = $"0 {edMinute} {edHour} ? * {dayOfWeek}"; // *"; // not verified
                }
                if (value.StartTime == 0 && value.EndTime == 0 && value.EdMonth > 0 && value.EdDay > 0)
                {
                    cronExpression = $"0 {edMinute} {edHour} {edDay} {edMonth} {dayOfWeek}"; // *"; // not verified
                }
            }
            //cronExpression = start ?
            //    $"0 {stMinute} {stHour} {stDay} {stMonth} {dayOfWeek}"
            //    :
            //    $"0 {edMinute} {edHour} {edDay} {edMonth} ?";
        }

        cronExpression = cronExpression.Replace("?", "*/1"); // Crontab doesn't support ?, so we replace it with */1 instead

        return cronExpression;
    }

    public List<uint> GetMatchingPeriods2()
    {
        var matchingPeriods = new List<uint>();
        var now = DateTime.Now;

        foreach (var period in _scheduleItems.Values)
        {
            var startDate = new DateTime((int)period.StYear, (int)period.StMonth, (int)period.StDay, (int)period.StHour, (int)period.StMin, 0);
            var endDate = new DateTime((int)period.EdYear, (int)period.EdMonth, (int)period.EdDay, (int)period.EdHour, (int)period.EdMin, 59);

            if (now >= startDate && now <= endDate)
            {
                matchingPeriods.Add(period.Id);
            }
        }

        return matchingPeriods;
    }

    public List<uint> GetMatchingPeriods()
    {
        var matchingPeriods = new List<uint>();
        var now = DateTime.UtcNow;

        foreach (var period in _scheduleItems.Values)
        {
            var startDate = new DateTime(
                period.StYear == 0 ? now.Year : (int)period.StYear,
                period.StMonth == 0 ? now.Month : (int)period.StMonth,
                period.StDay == 0 ? now.Day : (int)period.StDay,
                (int)period.StHour,
                (int)period.StMin,
                0
            );

            var endDate = new DateTime(
                period.EdYear == 0 ? now.Year : (int)period.EdYear,
                period.EdMonth == 0 ? now.Month : (int)period.EdMonth,
                period.EdDay == 0 ? now.Day : (int)period.EdDay,
                (int)period.EdHour,
                (int)period.EdMin,
                59
            );

            if (now >= startDate && now <= endDate && period.ActiveTake)
            {
                matchingPeriods.Add(period.Id);
            }
        }

        return matchingPeriods;
    }
}
