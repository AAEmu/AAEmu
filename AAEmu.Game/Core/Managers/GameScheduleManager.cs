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

    public bool CheckSpawnerInScheduleSpawners(int spawnerId)
    {
        return _gameScheduleSpawnerIds.ContainsKey(spawnerId);
    }

    public bool CheckDoodadInScheduleSpawners(int spawnerId)
    {
        return _gameScheduleDoodadIds.ContainsKey(spawnerId);
    }

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

    public enum PeriodStatus
    {
        NotFound,   // If doodadId or spawnerId not found
        NotStarted, // The period has not started
        InProgress, // Period in progress
        Ended       // The period has ended
    }

    /// <summary>
    /// Returns enum that shows the overall period status for all GameSchedules associated with spawnerId.
    /// </summary>
    /// <param name="spawnerId"></param>
    /// <returns></returns>
    public PeriodStatus GetPeriodStatusNpc(int spawnerId)
    {
        if (!_gameScheduleSpawnerIds.TryGetValue(spawnerId, out var ids))
            return PeriodStatus.NotFound; // If spawnerId is not found

        return CheckPeriodStatus(ids);
    }

    /// <summary>
    /// Returns an enum that shows the overall period status for all GameSchedules associated with the specified doodadId.
    /// If the doodadId is not found, returns <see cref="PeriodStatus.NotFound"/>.
    /// </summary>
    /// <param name="doodadId">The ID of the doodad to check.</param>
    /// <returns>The overall period status.</returns>
    public PeriodStatus GetPeriodStatusDoodad(int doodadId)
    {
        if (!_gameScheduleDoodadIds.TryGetValue(doodadId, out var ids))
            return PeriodStatus.NotFound; // If doodadId is not found

        return CheckPeriodStatus(ids);
    }

    /// <summary>
    /// Checks the period status for a list of game schedule IDs.
    /// </summary>
    /// <param name="ids">The list of game schedule IDs to check.</param>
    /// <returns>The overall period status.</returns>
    private PeriodStatus CheckPeriodStatus(List<int> ids)
    {
        // var hasNotStarted = true;  // Assume that no period has started
        var hasInProgress = false; // Assume that no period is in progress
        var hasEnded = false;      // Assume that no period has ended

        foreach (var gameScheduleId in ids)
        {
            if (_gameSchedules.TryGetValue(gameScheduleId, out var gs))
            {
                var (started, ended) = CheckData(gs);

                if (started && !ended)
                {
                    hasInProgress = true;  // The period has started, but has not yet ended
                    // hasNotStarted = false; // At least one period has started, so "hasn't started" = false
                }
                else if (ended)
                {
                    hasEnded = true;       // At least one period has ended
                    // hasNotStarted = false; // At least one period has ended, so "hasn't started" = false
                }
            }
        }

        // Determine the final status
        if (hasInProgress)
            return PeriodStatus.InProgress;
        if (hasEnded)
            return PeriodStatus.Ended;
        return PeriodStatus.NotStarted;
    }

    public string GetCronRemainingTime(int spawnerId, bool start = true)
    {
        var cronExpression = Empty;
        if (!_gameScheduleSpawnerIds.TryGetValue(spawnerId, out var _gameScheduleIds))
        {
            return cronExpression;
        }

        foreach (var gameScheduleId in _gameScheduleIds)
        {
            if (!_gameSchedules.ContainsKey(gameScheduleId)) { continue; }

            var gameSchedules = _gameSchedules[gameScheduleId];

            try
            {
                cronExpression = start ? GetCronExpression(gameSchedules, true) : GetCronExpression(gameSchedules, false);
            }
            catch (Exception e)
            {
                Logger.Info($"Error Spawning Npc {e}");
                throw;
            }
        }

        return cronExpression;
    }

    public string GetDoodadCronRemainingTime(int doodadId, bool start = true)
    {
        var cronExpression = Empty;
        if (!_gameScheduleDoodadIds.TryGetValue(doodadId, out var value))
        {
            return cronExpression;
        }

        foreach (var gameScheduleId in value)
        {
            if (!_gameSchedules.ContainsKey(gameScheduleId)) { continue; }

            var gameSchedules = _gameSchedules[gameScheduleId];

            cronExpression = start ? GetCronExpression(gameSchedules, true) : GetCronExpression(gameSchedules, false);
        }

        return cronExpression;
    }

    public TimeSpan GetRemainingTime(int spawnerId, bool start = true)
    {
        if (!_gameScheduleSpawnerIds.TryGetValue(spawnerId, out var value))
        {
            return TimeSpan.Zero;
        }

        var remainingTime = TimeSpan.MaxValue;

        foreach (var gameScheduleId in value)
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
        _gameScheduleSpawnerIds = [];
        foreach (var gss in _gameScheduleSpawners.Values)
        {
            if (!_gameScheduleSpawnerIds.TryGetValue(gss.SpawnerId, out var gameScheduleIds))
            {
                _gameScheduleSpawnerIds.Add(gss.SpawnerId, [gss.GameScheduleId]);
            }
            else
            {
                gameScheduleIds.Add(gss.GameScheduleId);
            }
        }

        // Doodads
        _gameScheduleDoodadIds = [];
        foreach (var gsd in _gameScheduleDoodads.Values)
        {
            if (!_gameScheduleDoodadIds.TryGetValue(gsd.DoodadId, out var gameScheduleIds))
            {
                _gameScheduleDoodadIds.Add(gsd.DoodadId, [gsd.GameScheduleId]);
            }
            else
            {
                gameScheduleIds.Add(gsd.GameScheduleId);
            }
        }
        //TODO: quests data
    }

    public bool GetGameScheduleDoodadsData(uint doodadId)
    {
        GameScheduleId = [];
        foreach (var gsd in _gameScheduleDoodads.Values)
        {
            if (gsd.DoodadId != doodadId) { continue; }
            GameScheduleId.Add(gsd.GameScheduleId);
        }
        return GameScheduleId.Count != 0;
    }

    public bool GetGameScheduleQuestsData(uint questId)
    {
        GameScheduleId = [];
        foreach (var gsq in _gameScheduleQuests.Values)
        {
            if (gsq.QuestId != questId) { continue; }
            GameScheduleId.Add(gsq.GameScheduleId);
        }
        return GameScheduleId.Count != 0;
    }

    private static (bool hasStarted, bool hasEnded) CheckData(GameSchedules value)
    {
        var now = DateTime.UtcNow;
        var currentTime = now.TimeOfDay;
        var currentDate = now.Date;

        // Преобразуем стандартный DayOfWeek в ваш кастомный DayOfWeek
        var currentDayOfWeek = (DayOfWeek)((int)now.DayOfWeek + 1);

        // Проверка на нулевые дату и месяц
        var startDate = value is { StYear: > 0, StMonth: > 0, StDay: > 0 }
            ? new DateTime(value.StYear, value.StMonth, value.StDay)
            : DateTime.MinValue;

        var endDate = value is { EdYear: > 0, EdMonth: > 0, EdDay: > 0 }
            ? new DateTime(value.EdYear, value.EdMonth, value.EdDay)
            : DateTime.MaxValue;

        var startTime = new TimeSpan(value.StartTime, value.StartTimeMin, 0);
        var endTime = new TimeSpan(value.EndTime, value.EndTimeMin, 0);

        var hasStarted = false;
        var hasEnded = false;

        // Проверка на попадание в период по дате и времени
        if ((startDate == DateTime.MinValue || currentDate > startDate || (currentDate == startDate && currentTime >= startTime)) &&
            (endDate == DateTime.MaxValue || currentDate < endDate || (currentDate == endDate && currentTime <= endTime)))
        {
            // Проверка на попадание в период по дню недели
            if (currentDayOfWeek == value.DayOfWeekId || value.DayOfWeekId == DayOfWeek.Invalid)
            {
                hasStarted = true;
            }
        }

        // Проверка на окончание периода
        if (endDate != DateTime.MaxValue && (currentDate > endDate || (currentDate == endDate && currentTime >= endTime)))
        {
            hasEnded = true;
        }

        return (hasStarted, hasEnded);
    }

    private static (bool hasStarted, bool hasEnded) CheckData0(GameSchedules value)
    {
        var now = DateTime.UtcNow;
        var currentTime = now.TimeOfDay;
        var currentDate = now.Date;

        var startDate = new DateTime(value.StYear, value.StMonth, value.StDay);
        var endDate = new DateTime(value.EdYear, value.EdMonth, value.EdDay);
        var startTime = new TimeSpan(value.StartTime, value.StartTimeMin, 0);
        var endTime = new TimeSpan(value.EndTime, value.EndTimeMin, 0);

        var hasStarted = false;
        var hasEnded = false;

        // Check if the period has started
        if (currentDate > startDate || (currentDate == startDate && currentTime >= startTime))
        {
            hasStarted = true;
        }

        // Check if the period has ended
        if (currentDate > endDate || (currentDate == endDate && currentTime >= endTime))
        {
            hasEnded = true;
        }

        return (hasStarted, hasEnded);
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
            Cron-выражение состоит из 6 или 7 полей:
            1. Секунды (0-59)
            2. Минуты (0-59)
            3. Часы (0-23)
            4. День месяца (1-31)
            5. Месяц (1-12)
            6. День недели (0-7, где 0 и 7 — воскресенье)
            7. Год (опционально, не поддерживается в стандартных cron)
        */

        // Convert DayOfWeek to cron format (0-7, where 0 and 7 are Sunday)
        var dayOfWeek = value.DayOfWeekId switch
        {
            DayOfWeek.Sunday => 0,
            DayOfWeek.Monday => 1,
            DayOfWeek.Tuesday => 2,
            DayOfWeek.Wednesday => 3,
            DayOfWeek.Thursday => 4,
            DayOfWeek.Friday => 5,
            DayOfWeek.Saturday => 6,
            _ => (int)DayOfWeek.Invalid // Use Invalid to mean "not specified".
        };

        // Get time and date values
        var stMonth = value.StMonth;
        var stDay = value.StDay;
        var stHour = start ? value.StHour : value.EdHour;
        var stMinute = start ? value.StMin : value.EdMin;

        var edMonth = value.EdMonth;
        var edDay = value.EdDay;
        var edHour = value.EdHour;
        var edMinute = value.EdMin;

        var startTime = value.StartTime;
        var startTimeMin = value.StartTimeMin;
        var endTime = value.EndTime;
        var endTimeMin = value.EndTimeMin;

        string cronExpression;

        if (value.DayOfWeekId == DayOfWeek.Invalid)
        {
            switch (start)
            {
                case true:
                    {
                        switch (value)
                        {
                            case { StartTime: 0, EndTime: 0, StMonth: 0, StDay: 0, StHour: 0 }:
                                {
                                    cronExpression = BuildCronExpression(0, 0, 0, stDay, stMonth, (int)DayOfWeek.Invalid);
                                    break;
                                }
                            case { EndTime: > 0, StMonth: 0, StDay: 0, StHour: 0 }:
                            case { EndTime: > 0, StMonth: > 0, StDay: > 0 }:
                                {
                                    cronExpression = BuildCronExpression(0, startTimeMin, startTime, stDay, stMonth, (int)DayOfWeek.Invalid);
                                    break;
                                }
                            //case { StartTime: 0, EndTime: 0, StMonth: 0, StDay: 0 }:
                            //case { StartTime: 0, EndTime: 0, StMonth: > 0, StDay: > 0 }:
                            //    {
                            //        cronExpression = BuildCronExpression(0, stMinute, stHour, stDay, stMonth, (int)DayOfWeek.Invalid);
                            //        break;
                            //    }
                            default:
                                {
                                    cronExpression = BuildCronExpression(0, stMinute, stHour, stDay, stMonth, (int)DayOfWeek.Invalid);
                                    break;
                                }
                        }

                        break;
                    }
                default:
                    {
                        switch (value)
                        {
                            case { StartTime: 0, EndTime: 0, EdMonth: 0, EdDay: 0, EdHour: 0 }:
                                {
                                    cronExpression = BuildCronExpression(0, 0, 0, edDay, edMonth, (int)DayOfWeek.Invalid);
                                    break;
                                }
                            case { EndTime: > 0, EdMonth: 0, EdDay: 0, EdHour: 0 }:
                            case { EndTime: > 0, EdMonth: > 0, EdDay: > 0 }:
                                {
                                    cronExpression = BuildCronExpression(0, endTimeMin, endTime, edDay, edMonth, (int)DayOfWeek.Invalid);
                                    break;
                                }
                            //case { StartTime: 0, EndTime: 0, EdMonth: 0, EdDay: 0 }:
                            //case { StartTime: 0, EndTime: 0, EdMonth: > 0, EdDay: > 0 }:
                            //    {
                            //        cronExpression = BuildCronExpression(0, edMinute, edHour, edDay, edMonth, (int)DayOfWeek.Invalid);
                            //        break;
                            //    }
                            default:
                                {
                                    cronExpression = BuildCronExpression(0, edMinute, edHour, edDay, edMonth, (int)DayOfWeek.Invalid);
                                    break;
                                }
                        }

                        break;
                    }
            }
        }
        else
        {
            switch (start)
            {
                case true:
                    {
                        switch (value)
                        {
                            case { StartTime: 0, EndTime: 0, StMonth: 0, StDay: 0, StHour: 0 }:
                                {
                                    cronExpression = BuildCronExpression(0, 0, 0, stDay, stMonth, dayOfWeek);
                                    break;
                                }
                            case { EndTime: > 0, StMonth: 0, StDay: 0, StHour: 0 }:
                            case { EndTime: > 0, StMonth: > 0, StDay: > 0 }:
                                {
                                    cronExpression = BuildCronExpression(0, startTimeMin, startTime, stDay, stMonth, dayOfWeek);
                                    break;
                                }
                            //case { StartTime: 0, EndTime: 0, StMonth: 0, StDay: 0 }:
                            //case { StartTime: 0, EndTime: 0, StMonth: > 0, StDay: > 0 }:
                            //    {
                            //        cronExpression = BuildCronExpression(stMinute, stHour, stDay, stMonth, dayOfWeek);
                            //        break;
                            //    }
                            default:
                                {
                                    cronExpression = BuildCronExpression(0, stMinute, stHour, stDay, stMonth, dayOfWeek);
                                    break;
                                }
                        }

                        break;
                    }
                default:
                    {
                        switch (value)
                        {
                            case { StartTime: 0, EndTime: 0, EdMonth: 0, EdDay: 0, EdHour: 0 }:
                                {
                                    cronExpression = BuildCronExpression(0, 0, 0, edDay, edMonth, dayOfWeek);
                                    break;
                                }
                            case { EndTime: > 0, EdMonth: 0, EdDay: 0, EdHour: 0 }:
                            case { EndTime: > 0, EdMonth: > 0, EdDay: > 0 }:
                                {
                                    cronExpression = BuildCronExpression(0, endTimeMin, endTime, edDay, edMonth, dayOfWeek);
                                    break;
                                }
                            //case { StartTime: 0, EndTime: 0, EdMonth: 0, EdDay: 0 }:
                            //case { StartTime: 0, EndTime: 0, EdMonth: > 0, EdDay: > 0 }:
                            //    {
                            //        cronExpression = BuildCronExpression(edMinute, edHour, edDay, edMonth, dayOfWeek);
                            //        break;
                            //    }
                            default:
                                {
                                    cronExpression = BuildCronExpression(0, edMinute, edHour, edDay, edMonth, dayOfWeek);
                                    break;
                                }
                        }

                        break;
                    }
            }
        }

        cronExpression = cronExpression.Replace("?", "*"); // Crontab doesn't support ?, so we replace it with */1 instead

        return cronExpression;

        // Local function for forming cron-expression
        string BuildCronExpression(int seconds, int minute, int hour, int day, int month, int dayOfWeekCron)
        {
            return dayOfWeek switch
            {
                8 => $"{seconds} {minute} {hour} {day} {month} *",
                _ => $"{seconds} {minute} {hour} {day} {month} {dayOfWeekCron}"
            };
        }
    }
}
