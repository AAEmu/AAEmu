using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Sagas;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.GameData;

/// <summary>
/// Loads saga_quest_groups + saga_quests into a validated <see cref="SagaQuestCatalog"/>. Quest
/// existence is checked against QuestManager's already-loaded templates (GameDataManager completes
/// the quest manager's Load before it runs the game-data loaders).
/// </summary>
[GameData]
public class SagaQuestGameData : Singleton<SagaQuestGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private SagaQuestCatalog _catalog = SagaQuestCatalog.Empty;

    public SagaQuestCatalog Catalog => _catalog;

    public void Load(SqliteConnection connection)
    {
        List<SagaGroupRow> groupRows = [];
        using (var command = connection.CreateCommand())
        {
            // Shipped group ids are not contiguous (5..7 are absent), so the row order is the id order.
            command.CommandText = "SELECT * FROM saga_quest_groups ORDER BY id";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                groupRows.Add(new SagaGroupRow(
                    reader.GetUInt32("id"),
                    reader.GetString("name") ?? string.Empty,
                    reader.GetUInt32("currency_id", 0),
                    reader.GetUInt32("currency_value", 0),
                    reader.GetUInt32("item_set_id", 0),
                    reader.GetUInt32("milestone_id", 0),
                    reader.GetUInt32("book_id", 0),
                    reader.GetUInt32("completion_cond_id", 0)));
            }
        }

        List<SagaMembershipRow> membershipRows = [];
        using (var command = connection.CreateCommand())
        {
            // saga_quests has no order column; its shipped row order is the progression order
            // (chapter_idx/quest_idx follow it in every group, with one authored duplicate that
            // only the row order can break).
            command.CommandText =
                "SELECT rowid, saga_quest_group_id, quest_context_id FROM saga_quests ORDER BY rowid";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                membershipRows.Add(new SagaMembershipRow(
                    reader.GetUInt32("saga_quest_group_id"),
                    reader.GetUInt32("quest_context_id")));
            }
        }

        _catalog = SagaQuestCatalog.Build(
            groupRows,
            membershipRows,
            questId => QuestManager.Instance.GetTemplate(questId) != null);

        foreach (var issue in _catalog.Issues)
        {
            Logger.Error(
                "Saga content rejected: group {0}, quest {1} — {2}",
                issue.SagaQuestGroupId, issue.QuestContextId, issue.Reason);
        }

        Logger.Info(
            "Loaded {0} saga quest groups ({1} quests, {2} rejected rows)",
            _catalog.Groups.Count,
            _catalog.Groups.Sum(group => group.OrderedQuestIds.Count),
            _catalog.Issues.Count);
    }

    public void PostLoad()
    {
    }
}
