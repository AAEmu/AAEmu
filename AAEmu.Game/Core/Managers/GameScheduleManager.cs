using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Schedules;

using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class GameScheduleManager : Singleton<GameScheduleManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<int, GameSchedules> _gameSchedules; // GameScheduleId, GameSchedules
        private Dictionary<int, GameScheduleSpawners> _gameScheduleSpawners;
        private List<int> GameScheduleId { get; set; }

        public void LoadGameSchedules(Dictionary<int, GameSchedules> gameSchedules)
        {
            _gameSchedules = new Dictionary<int, GameSchedules>();
            foreach (var gs in gameSchedules)
            {
                _gameSchedules.TryAdd(gs.Key, gs.Value);
            }
        }

        public void LoadGameScheduleSpawners(Dictionary<int, GameScheduleSpawners> gameScheduleSpawners)
        {
            _gameScheduleSpawners = new Dictionary<int, GameScheduleSpawners>();
            foreach (var gss in gameScheduleSpawners)
            {
                _gameScheduleSpawners.TryAdd(gss.Key, gss.Value);
            }
        }

        public bool CheckInGameSchedules(uint npcSpawnerId)
        {
            if (!GetGameScheduleSpawnersData(npcSpawnerId)) { return false; }
            var res = (from gameScheduleId in GameScheduleId
                       where _gameSchedules.ContainsKey(gameScheduleId)
                       select _gameSchedules[gameScheduleId]
                into gs
                       select CheckData(gs)).ToList();

            return res.Contains(true);
        }

        public bool GetGameScheduleSpawnersData(uint npcSpawnerId)
        {
            GameScheduleId = new List<int>();
            foreach (var gss in _gameScheduleSpawners.Values)
            {
                if (gss.SpawnerId != npcSpawnerId) { continue; }
                GameScheduleId.Add(gss.GameScheduleId);
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
            var curDayOfWeek = (Models.Game.Schedules.DayOfWeek)DateTime.UtcNow.DayOfWeek;

            if (value.DayOfWeekId == Models.Game.Schedules.DayOfWeek.Invalid)
            {
                if (curYear >= value.StYear && curMonth >= value.StMonth && curDay >= value.StDay && curHours >= value.StHour && curMinutes >= value.StMin &&
                    curYear <= value.EdYear && curMonth <= value.EdMonth && curDay <= value.EdDay && curHours <= value.EdHour && curMinutes <= value.EdMin)
                {
                    return true;
                }
            }
            else
            {
                if (curDayOfWeek == value.DayOfWeekId &&
                    curYear >= value.StYear && curMonth >= value.StMonth && curDay >= value.StDay && curHours >= value.StHour && curMinutes >= value.StMin &&
                    curYear <= value.EdYear && curMonth <= value.EdMonth && curDay <= value.EdDay && curHours <= value.EdHour && curMinutes <= value.EdMin)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
