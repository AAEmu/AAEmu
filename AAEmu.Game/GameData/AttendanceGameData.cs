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
    private Dictionary<int, Dictionary<int, List<AccountAttendanceReward>>> _rewards;
    private Dictionary<int, Dictionary<int, List<AccountAttendanceReward>>> _additionalRewards;

    public void Load(SqliteConnection connection, SqliteConnection connection2)
    {
        _rewards = new Dictionary<int, Dictionary<int, List<AccountAttendanceReward>>>();
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

    public (uint, int) GetAdditionalReward(int year, int month, int dayCount)
    {
        var itemId = 0u;
        var countItem = 0;

        foreach (KeyValuePair<int, Dictionary<int, List<AccountAttendanceReward>>> ary in _rewards)
        {
            if (ary.Key == year)
                foreach (KeyValuePair<int, List<AccountAttendanceReward>> ar in ary.Value)
                {
                    if (ar.Key == month)
                        foreach (AccountAttendanceReward reward in ar.Value)
                        {
                            if (reward.DayCount >= dayCount && reward.AdditionalReward)
                            {
                                itemId = reward.ItemId;
                                countItem = reward.ItemCount;
                            }
                        }
                }
        }

        return (itemId, countItem);
    }
}
