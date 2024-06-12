using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Attendance;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

[GameData]
public class AttendanceGameData : Singleton<AttendanceGameData>, IGameDataLoader
{
    // подарки         год             месяц     по дням
    private Dictionary<int, Dictionary<int, List<AccountAttendanceReward>>> _rewards;
    // доп подарки     год             месяц     по дням
    private Dictionary<int, Dictionary<int, List<AccountAttendanceReward>>> _additionalRewards;

    //public void Load2(SqliteConnection connection, SqliteConnection connection2)
    //{
    //    // подарки                год             месяц     по дням
    //    _rewards = new Dictionary<int, Dictionary<int, List<AccountAttendanceReward>>>();
    //    // доп подарки                      год             месяц     по дням
    //    _additionalRewards = new Dictionary<int, Dictionary<int, List<AccountAttendanceReward>>>();
    //    // посещения          characterId  DayCount
    //    _attendances = new Dictionary<int, AccountAttendanceReward>();

    //    var monthRewards = new Dictionary<int, List<AccountAttendanceReward>>();
    //    var monthAdittionalRewards = new Dictionary<int, List<AccountAttendanceReward>>();
    //    var rewards = new List<AccountAttendanceReward>();
    //    var adittionalRewards = new List<AccountAttendanceReward>();

    //    var indx = 0;

    //    using var command = connection.CreateCommand();
    //    command.CommandText = "SELECT * FROM account_attendance_rewards ORDER BY year, month;";
    //    command.Prepare();
    //    using var sqliteReader = command.ExecuteReader();
    //    using var reader = new SQLiteWrapperReader(sqliteReader);
    //    while (reader.Read())
    //    {
    //        var template = new AccountAttendanceReward();
    //        template.Id = reader.GetUInt32("id");
    //        template.AdditionalReward = reader.GetBoolean("additional_reward", false);
    //        template.DayCount = reader.GetInt32("day_count");
    //        template.ItemCount = reader.GetInt32("item_count");
    //        template.ItemGradeId = reader.GetInt32("item_grade_id");
    //        template.ItemId = reader.GetUInt32("item_id");
    //        template.Month = reader.GetInt32("month");
    //        template.Year = reader.GetInt32("year");

    //        if (template.AdditionalReward)
    //        {
    //            _additionalRewards[template.Year][template.Month].Add(template);
    //        }
    //        else
    //        {
    //            if (_rewards.TryGetValue(template.Year, out monthRewards))
    //            {
    //                if (monthRewards.TryGetValue(template.Month, out rewards))
    //                {
    //                    _rewards[template.Year][template.Month].Add(template);
    //                }
    //                else
    //                {

    //                }
    //            }
    //            else
    //            {
    //                monthRewards = new Dictionary<int, List<AccountAttendanceReward>>();
    //                rewards.Add(template);
    //                monthRewards.TryAdd(template.Month, rewards);
    //                _rewards.TryAdd(template.Year, monthRewards);
    //            }
    //        }

    //        indx++;
    //        if (indx <= 25)
    //        {
    //            rewards.Add(template);
    //        }
    //        else if (indx <= 28)
    //        {
    //            adittionalRewards.Add(template);
    //        }

    //        if (indx == 28 && template.Month <= 12)
    //        {
    //            var res = monthRewards.TryAdd(template.Month, rewards);
    //            monthAdittionalRewards.TryAdd(template.Month, adittionalRewards);

    //            rewards = new List<AccountAttendanceReward>();
    //            adittionalRewards = new List<AccountAttendanceReward>();
    //            indx = 0;
    //        }
    //        if (indx == 0 && template.Month == 12)
    //        {
    //            _rewards.TryAdd(template.Year, monthRewards);
    //            _additionalRewards.TryAdd(template.Year, monthAdittionalRewards);

    //            monthRewards = new Dictionary<int, List<AccountAttendanceReward>>();
    //            monthAdittionalRewards = new Dictionary<int, List<AccountAttendanceReward>>();
    //        }
    //    }
    //}

    public void Load(SqliteConnection connection, SqliteConnection connection2)
    {
        // подарки                год             месяц     по дням
        _rewards = new Dictionary<int, Dictionary<int, List<AccountAttendanceReward>>>();
        // доп подарки                      год             месяц     по дням
        _additionalRewards = new Dictionary<int, Dictionary<int, List<AccountAttendanceReward>>>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM account_attendance_rewards ORDER BY year, month;";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var template = new AccountAttendanceReward();
            template.Id = reader.GetUInt32("id");
            template.AdditionalReward = reader.GetBoolean("additional_reward", false);
            template.DayCount = reader.GetInt32("day_count");
            template.ItemCount = reader.GetInt32("item_count");
            template.ItemGradeId = reader.GetInt32("item_grade_id");
            template.ItemId = reader.GetUInt32("item_id");
            template.Month = reader.GetInt32("month");
            template.Year = reader.GetInt32("year");

            // Check if the template has an additional reward
            if (template.AdditionalReward)
            {
                // Check if _additionalRewards contains a dictionary for the template's year
                if (!_additionalRewards.ContainsKey(template.Year))
                {
                    // If not, create a new dictionary for that year
                    _additionalRewards[template.Year] = new Dictionary<int, List<AccountAttendanceReward>>();
                }

                // Check if the dictionary for the year contains a list for the template's month
                if (!_additionalRewards[template.Year].ContainsKey(template.Month))
                {
                    // If not, create a new list for that month
                    _additionalRewards[template.Year][template.Month] = new List<AccountAttendanceReward>();
                }

                // Add the template to the list for the month
                _additionalRewards[template.Year][template.Month].Add(template);
            }
            else
            {
                // If the template does not have an additional reward, follow the same process but for _rewards
                if (!_rewards.ContainsKey(template.Year))
                {
                    _rewards[template.Year] = new Dictionary<int, List<AccountAttendanceReward>>();
                }

                if (!_rewards[template.Year].ContainsKey(template.Month))
                {
                    _rewards[template.Year][template.Month] = new List<AccountAttendanceReward>();
                }

                _rewards[template.Year][template.Month].Add(template);
            }
        }
    }

    public void PostLoad()
    {
    }

    public (uint, int) GetReward(int year, int month, int dayCount)
    {
        var itemId = 0u;
        var countItem = 0;

        foreach (var reward in _rewards.Where(ary => ary.Key == year)
                     .SelectMany(ary => ary.Value.Where(ar => ar.Key == month)
                         .SelectMany(ar => ar.Value.Where(reward => reward.DayCount == dayCount))))
        {
            itemId = reward.ItemId;
            countItem = reward.ItemCount;
        }
        return (itemId, countItem);
    }
}
