using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Milestones;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.GameData;

/// <summary>
/// Loads the shipped <c>milestones</c> rows (the release-window columns that gate advancement)
/// plus the reversed quest → milestone trigger index. The triggers are read from QuestManager's
/// already-loaded templates — quest_contexts.milestone_id is the same column QuestManager loads
/// into QuestTemplate.MilestoneId, so the index cannot drift from what quest completion sees.
/// </summary>
[GameData]
public class MilestoneGameData : Singleton<MilestoneGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private MilestoneCatalog _catalog = MilestoneCatalog.Empty;

    public MilestoneCatalog Catalog => _catalog;

    public void Load(SqliteConnection connection)
    {
        List<MilestoneRow> rows = [];
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, start_date, end_date, release FROM milestones ORDER BY id";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var id = reader.GetUInt32("id");
                // Both window bounds are required content: a row without them cannot decide its
                // own eligibility, so it is rejected loudly instead of defaulted.
                if (reader.IsDBNull("start_date") || reader.IsDBNull("end_date"))
                {
                    Logger.Error(
                        "Milestone content rejected: row {0} has no start/end date — row dropped", id);
                    continue;
                }

                rows.Add(new MilestoneRow(
                    id,
                    ServerCalendar.AsUtc(reader.GetDateTime("start_date")),
                    ServerCalendar.AsUtc(reader.GetDateTime("end_date")),
                    reader.GetBoolean("release")));
            }
        }

        // The reversed trigger index: quest → milestone, straight from the loaded quest templates.
        List<MilestoneTriggerRow> triggers = [];
        foreach (var template in QuestManager.Instance.GetTemplates())
        {
            if (template.MilestoneId != 0)
                triggers.Add(new MilestoneTriggerRow(template.Id, template.MilestoneId));
        }

        _catalog = MilestoneCatalog.Build(rows, triggers);

        foreach (var issue in _catalog.Issues)
        {
            Logger.Error(
                "Milestone content rejected: quest {0}, milestone {1} — {2}",
                issue.QuestId, issue.MilestoneId, issue.Reason);
        }

        var triggerCount = _catalog.Rows.Sum(row => _catalog.GetQuestChain(row.Id).Count);
        Logger.Info(
            "Loaded {0} milestones ({1} reversed quest triggers, {2} rejected rows)",
            _catalog.Rows.Count, triggerCount, _catalog.Issues.Count);
    }

    public void PostLoad()
    {
    }
}
