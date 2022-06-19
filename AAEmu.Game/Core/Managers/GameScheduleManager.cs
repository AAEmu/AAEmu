using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Schedules;

using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class GameScheduleManager : Singleton<GameScheduleManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<int, GameSchedules> _gameSchedules; // GameScheduleId, GameSchedules
        private Dictionary<int, GameScheduleSpawners> _gameScheduleSpawners;
        private Dictionary<int, GameScheduleDoodads> _gameScheduleDoodads;
        private Dictionary<int, GameScheduleQuests> _gameScheduleQuests;
        private List<int> GameScheduleId { get; set; }

        public void Load()
        {
            _log.Info("Loading shchedules...");

            SchedulesGameData.Instance.PostLoad();

            _log.Info("Loaded shchedules");
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

        public bool CheckSpawnerInGameSchedules(uint spawnerId)
        {
            if (!GetGameScheduleSpawnersData(spawnerId)) { return false; }
            var res = CheckScheduler();
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

        private List<bool> CheckScheduler()
        {
            var res = (from gameScheduleId in GameScheduleId
                       where _gameSchedules.ContainsKey(gameScheduleId)
                       select _gameSchedules[gameScheduleId]
                into gs
                       select CheckData(gs)).ToList();
            return res;
        }

        public bool GetGameScheduleSpawnersData(uint spawnerId)
        {
            GameScheduleId = new List<int>();
            foreach (var gss in _gameScheduleSpawners.Values)
            {
                if (gss.SpawnerId != spawnerId) { continue; }
                GameScheduleId.Add(gss.GameScheduleId);
            }
            return GameScheduleId.Count != 0;
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
            var curDayOfWeek = (Models.Game.Schedules.DayOfWeek)DateTime.UtcNow.DayOfWeek + 1;

            if (value.DayOfWeekId == Models.Game.Schedules.DayOfWeek.Invalid)
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
    }
}
