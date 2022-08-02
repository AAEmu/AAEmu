using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Schedules;

using NLog;

using DayOfWeek = AAEmu.Game.Models.Game.Schedules.DayOfWeek;

namespace AAEmu.Game.Core.Managers
{
    public class GameScheduleManager : Singleton<GameScheduleManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private bool _loaded = false;
        private Dictionary<int, GameSchedules> _gameSchedules; // GameScheduleId, GameSchedules
        private Dictionary<int, GameScheduleSpawners> _gameScheduleSpawners;
        private Dictionary<int, List<int>> _gameScheduleSpawnerIds;
        private Dictionary<int, GameScheduleDoodads> _gameScheduleDoodads;
        private Dictionary<int, GameScheduleQuests> _gameScheduleQuests;
        private List<int> GameScheduleId { get; set; }

        public void Load()
        {
            if (_loaded)
                return;
            
            _log.Info("Loading schedules...");

            SchedulesGameData.Instance.PostLoad();

            LoadGameScheduleSpawnersData();

            _log.Info("Loaded schedules");

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

        public bool CheckSpawnerInGameSchedules(int spawnerId)
        {
            var res = CheckScheduler(spawnerId);
            return res.Contains(true);
        }

        public bool CheckDoodadInGameSchedules(uint doodadId)
        {
            if (!GetGameScheduleDoodadsData(doodadId)) { return false; }
            var res = CheckScheduler();
            return res.Contains(true);
        }

        public bool CheckQuestInGameSchedules(uint questId)
        {
            if (!GetGameScheduleQuestsData(questId)) { return false; }
            var res = CheckScheduler();
            return res.Contains(true);
        }

        private List<bool> CheckScheduler(int spawnerId)
        {
            if (!_gameScheduleSpawnerIds.ContainsKey(spawnerId))
            {
                return new List<bool>();
            }

            var res = (from gameScheduleId in _gameScheduleSpawnerIds[spawnerId]
                       where _gameSchedules.ContainsKey(gameScheduleId)
                       select _gameSchedules[gameScheduleId]
                into gs
                       select CheckData(gs)).ToList();
            return res;
        }

        private List<bool> CheckScheduler()
        {
            var res = (from gameScheduleId in GameScheduleId
                       where _gameSchedules.ContainsKey(gameScheduleId)
                       select _gameSchedules[gameScheduleId]
                into gs
                       select CheckData(gs)).ToList();

            return res;
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
            _gameScheduleSpawnerIds = new Dictionary<int, List<int>>();

            // Spawners
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
            foreach (var gsd in _gameScheduleDoodads.Values)
            {
                if (!_gameScheduleSpawnerIds.ContainsKey(gsd.DoodadId))
                {
                    _gameScheduleSpawnerIds.Add(gsd.DoodadId, new List<int> { gsd.GameScheduleId });
                }
                else
                {
                    _gameScheduleSpawnerIds[gsd.DoodadId].Add(gsd.GameScheduleId);
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

        private bool CheckData(GameSchedules value)
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

        private TimeSpan GetRemainingTimeStart(GameSchedules value)
        {
            TimeSpan remainingDate;
            DateTime otherDate;
            var year = value.StYear;
            var month = value.StMonth;
            var day = value.StDay;
            var hour = value.StHour;
            var minute = value.StMin;

            try
            {
                if (value.StartTime == 0)
                {
                    if (value.StYear == 0) { year = DateTime.UtcNow.Year; }
                    if (value.StMonth == 0) { month = DateTime.UtcNow.Month; }
                    if (value.StDay == 0) { day = DateTime.UtcNow.Day; }
                    if (value.StHour == 0) { hour = DateTime.UtcNow.Hour; }
                }
                else
                {
                    year = DateTime.UtcNow.Year;
                    month = DateTime.UtcNow.Month;
                    day = DateTime.UtcNow.Day;
                    hour = value.StartTime;
                    minute = value.StartTimeMin;
                }

                otherDate = new DateTime(year, month, day, hour, minute, 0);

                remainingDate = otherDate - DateTime.UtcNow;
                if (remainingDate.TotalHours < 0)
                {
                    remainingDate += TimeSpan.FromHours(24);
                }
                if (remainingDate.Days < 0)
                {
                    remainingDate += TimeSpan.FromDays(365);
                }
            }
            catch (Exception)
            {
                remainingDate = TimeSpan.FromHours(24);
            }

            if (remainingDate == TimeSpan.Zero)
            {
                remainingDate = TimeSpan.FromHours(24);
            }
            return remainingDate;
        }
        
        private TimeSpan GetRemainingTimeEnd(GameSchedules value)
        {
            TimeSpan remainingDate;
            DateTime otherDate;
            var year = value.EdYear;
            var month = value.EdMonth;
            var day = value.EdDay;
            var hour = value.EdHour;
            var minute = value.EdMin;

            try
            {
                if (value.EndTime == 0)
                {
                    if (value.EdYear == 0) { year = DateTime.UtcNow.Year; }
                    if (value.EdMonth == 0) { month = DateTime.UtcNow.Month; }
                    if (value.EdDay == 0) { day = DateTime.UtcNow.Day; }
                    if (value.EdHour == 0) { hour = DateTime.UtcNow.Hour; }
                }
                else
                {
                    year = DateTime.UtcNow.Year;
                    month = DateTime.UtcNow.Month;
                    day = DateTime.UtcNow.Day;
                    hour = value.EndTime;
                    minute = value.EndTimeMin;
                }

                otherDate = new DateTime(year, month, day, hour, minute, 0);
                remainingDate = otherDate - DateTime.Now;
                if (remainingDate.Hours < 0)
                {
                    remainingDate = TimeSpan.FromHours(24) - remainingDate;
                }
            }
            catch (Exception)
            {
                remainingDate = TimeSpan.FromHours(1);
            }
            return remainingDate;
        }

        private TimeSpan GetRemainingTime(GameSchedules value)
        {
            var curHours = DateTime.UtcNow.TimeOfDay.Hours;
            var curMinutes = DateTime.UtcNow.TimeOfDay.Minutes;
            var curDayOfWeek = (DayOfWeek)DateTime.UtcNow.DayOfWeek + 1;

            var curDate = TimeSpan.FromHours(curHours) + TimeSpan.FromMinutes(curMinutes);
            TimeSpan otherDate;

            if (value.DayOfWeekId == DayOfWeek.Invalid)
            {
                if (value.EndTime == 0 && value.StHour == 0 && value.EdHour == 0)
                {
                    return TimeSpan.Zero;
                }
                if (value.StartTime > 0)
                {
                    otherDate = TimeSpan.FromHours(value.StartTime) + TimeSpan.FromMinutes(value.StartTimeMin);
                    return curDate - otherDate;
                }
                if (value.EndTime > 0)
                {
                    otherDate = TimeSpan.FromHours(value.EndTime) + TimeSpan.FromMinutes(value.EndTimeMin);
                    return curDate - otherDate;
                }
                if (value.StHour > 0)
                {
                    otherDate = TimeSpan.FromHours(value.StHour) + TimeSpan.FromMinutes(value.StMin);
                    return curDate - otherDate;
                }
                if (value.EdHour > 0)
                {
                    otherDate = TimeSpan.FromHours(value.EdHour) + TimeSpan.FromMinutes(value.EdMin);
                    return curDate - otherDate;
                }
            }
            else
            {
                if (curDayOfWeek == value.DayOfWeekId)
                {
                    if (value.EndTime == 0 && value.StHour == 0 && value.EdHour == 0)
                    {
                        return TimeSpan.Zero;
                    }
                    if (value.StartTime > 0)
                    {
                        otherDate = TimeSpan.FromHours(value.StartTime) + TimeSpan.FromMinutes(value.StartTimeMin);
                        return curDate - otherDate;
                    }
                    if (value.EndTime > 0)
                    {
                        otherDate = TimeSpan.FromHours(value.EndTime) + TimeSpan.FromMinutes(value.EndTimeMin);
                        return curDate - otherDate;
                    }
                    if (value.StHour > 0)
                    {
                        otherDate = TimeSpan.FromHours(value.StHour) + TimeSpan.FromMinutes(value.StMin);
                        return curDate - otherDate;
                    }
                    if (value.EdHour > 0)
                    {
                        otherDate = TimeSpan.FromHours(value.EdHour) + TimeSpan.FromMinutes(value.EdMin);
                        return curDate - otherDate;
                    }
                }
            }
            return TimeSpan.FromHours(1);
        }
    }
}
