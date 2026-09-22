using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Team.Recruitment;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.GameData;

/// <summary>
/// The raid recruitment board's content: the four raid_recruit_* tables behind the client's option readers
/// (X2Team:GetRaidRecruitTypeList, GetRaidRecruitSubTypeList, GetRaidRecruitHeadcountList,
/// GetRaidRecruitExpense) and content_configs max_gear_score_to_recruit (id 385, kind 40), the ceiling the
/// post dialog names gearScoreLimitMax.
/// </summary>
[GameData]
public class RaidRecruitGameData : Singleton<RaidRecruitGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public RaidRecruitContent Content { get; private set; } = RaidRecruitContent.Empty;

    public void Load(SqliteConnection connection)
    {
        var types = new Dictionary<int, RaidRecruitType>();
        var subTypes = new Dictionary<int, RaidRecruitSubType>();
        var headcounts = new List<int>();
        var bands = new List<RaidRecruitExpenseBand>();
        long maxGearScore = 0;

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, name, visible, icon_key FROM raid_recruit_types";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var id = reader.GetInt32("id");
                types[id] = new RaidRecruitType(id, reader.GetString("name"), reader.GetBoolean("visible"),
                    reader.GetString("icon_key"));
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT id, name, raid_recruit_type_id, level, comment, gear_score FROM raid_recruit_sub_types";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var id = reader.GetInt32("id");
                subTypes[id] = new RaidRecruitSubType(id, reader.GetString("name"),
                    reader.GetInt32("raid_recruit_type_id"), reader.GetInt32("level"), reader.GetString("comment"),
                    reader.GetInt32("gear_score"));
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT headcount FROM raid_recruit_headcounts ORDER BY headcount";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
                headcounts.Add(reader.GetInt32("headcount"));
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT id, \"min\", \"max\", expense FROM raid_recruit_time_and_expenses ORDER BY \"min\"";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
                bands.Add(new RaidRecruitExpenseBand(reader.GetInt32("id"), reader.GetInt32("min"),
                    reader.GetInt32("max"), reader.GetInt64("expense")));
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT c.value FROM content_configs c JOIN enum_content_configs e ON e.id = c.id " +
                "WHERE e.name = 'max_gear_score_to_recruit'";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            if (reader.Read())
                maxGearScore = reader.GetInt64("value");
        }

        Content = new RaidRecruitContent(types, subTypes, headcounts, bands, maxGearScore);
        Logger.Info("Loaded {0} raid recruit types, {1} sub types, {2} headcounts, {3} expense bands, max gear score {4}",
            types.Count, subTypes.Count, headcounts.Count, bands.Count, maxGearScore);
    }

    public void PostLoad()
    {
    }

    public void SetForTest(RaidRecruitContent content) => Content = content;
}
