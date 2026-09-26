using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.PlotAuctions;
using AAEmu.Game.Models.Game.SailingActivity;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.GameData;

/// <summary>
/// The sailing activity's content: <c>game_activities</c> (the id the packets carry as
/// <c>activityId</c>), <c>game_activity_stages</c> and <c>game_activity_tasks</c>.
/// </summary>
/// <remarks>
/// <para>
/// Shipped content is not internally consistent and this loader does not pretend otherwise. Two
/// tasks point at stages that do not exist, and a third group of tasks is unbound. Those rows are
/// reported through <see cref="Diagnostics"/> and error-logged, and loading continues — refusing to
/// boot over an authoring mistake in the shipped table would take the whole server down for a
/// cosmetic problem.
/// </para>
/// <para>
/// Rows that are genuinely malformed — an unparseable rewards cell, an unlock value that is not a
/// non-negative integer, a duplicate id — still fail the load, because a server that silently
/// drops a reward or an unlock threshold is worse than one that will not start.
/// </para>
/// </remarks>
[GameData]
public class SailingActivityGameData : Singleton<SailingActivityGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<int, SailingActivityRow> _activities = [];
    private readonly Dictionary<int, SailingActivityStageRow> _stages = [];
    private readonly List<SailingActivityTaskRow> _tasks = [];

    /// <summary>Orphans and other rows the loader reported instead of failing on.</summary>
    public SailingActivityDiagnostics Diagnostics { get; } = new();

    /// <summary>Every loaded activity, ordered by id.</summary>
    public IReadOnlyList<SailingActivityRow> Activities =>
        _activities.Values.OrderBy(x => x.Id).ToList();

    /// <summary>Every loaded stage, ordered by stage number then id.</summary>
    public IReadOnlyList<SailingActivityStageRow> Stages =>
        _stages.Values.OrderBy(x => x.StageNumber).ThenBy(x => x.Id).ToList();

    /// <summary>Every loaded task, ordered by sort order then id.</summary>
    public IReadOnlyList<SailingActivityTaskRow> Tasks => _tasks;

    public void Load(SqliteConnection connection)
    {
        _activities.Clear();
        _stages.Clear();
        _tasks.Clear();
        Diagnostics.Orphans.Clear();
        Diagnostics.Problems.Clear();
        Diagnostics.UnresolvedWindows.Clear();

        LoadActivities(connection);
        LoadStages(connection);
        LoadTasks(connection);

        Logger.Info(
            "Loaded {0} game activities ({1} sailing-windowed), {2} activity stages, {3} activity tasks " +
            "({4} orphan reference(s), {5} rejected row(s))",
            _activities.Count,
            _activities.Values.Count(x => x.StartUtc.HasValue),
            _stages.Count,
            _tasks.Count,
            Diagnostics.Orphans.Count,
            Diagnostics.Problems.Count);

        foreach (var orphan in Diagnostics.Orphans)
        {
            Logger.Error(
                "Sailing activity: {0} row {1} references {2} {3}, which is not loaded; the row is kept but unreachable",
                orphan.Table, orphan.RowId, orphan.MissingTable, orphan.MissingId);
        }

        foreach (var problem in Diagnostics.Problems)
            Logger.Error("Sailing activity: {0} row {1} rejected - {2}", problem.Table, problem.RowId, problem.Reason);

        foreach (var window in Diagnostics.UnresolvedWindows)
            Logger.Warn("Sailing activity: game_activities row {0} window unresolved - {1}", window.RowId, window.Reason);
    }

    public void PostLoad()
    {
    }

    private void LoadActivities(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, activity_name, status, time_mode, start_time, end_time, server_groups, task_group_id
            FROM game_activities
            """;
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var id = reader.GetInt32("id");
            var startRaw = reader.GetString("start_time");
            var endRaw = reader.GetString("end_time");

            SailingActivityRow row;
            if (SailingActivityWindow.TryResolve(startRaw, endRaw, out var startUtc, out var endUtc, out var reason))
            {
                row = new SailingActivityRow
                {
                    Id = id,
                    Name = reader.GetString("activity_name"),
                    IsActive = reader.GetBoolean("status"),
                    TimeMode = reader.GetInt32("time_mode"),
                    StartTimeRaw = startRaw,
                    EndTimeRaw = endRaw,
                    ServerGroupsRaw = reader.GetString("server_groups"),
                    TaskGroupId = reader.GetInt32("task_group_id"),
                    StartUtc = startUtc,
                    EndUtc = endUtc,
                };
            }
            else
            {
                Diagnostics.AddUnresolvedWindow(id, reason);
                row = new SailingActivityRow
                {
                    Id = id,
                    Name = reader.GetString("activity_name"),
                    IsActive = reader.GetBoolean("status"),
                    TimeMode = reader.GetInt32("time_mode"),
                    StartTimeRaw = startRaw,
                    EndTimeRaw = endRaw,
                    ServerGroupsRaw = reader.GetString("server_groups"),
                    TaskGroupId = reader.GetInt32("task_group_id"),
                };
            }

            if (!_activities.TryAdd(row.Id, row))
                throw new InvalidDataException($"game_activities has duplicate id {row.Id}");
        }
    }

    private void LoadStages(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, stage_name, stage_number, unlock_mode, time_mode, unlock_time, prerequisite_stage_id
            FROM game_activity_stages
            """;
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var id = reader.GetInt32("id");
            var unlockTimeRaw = reader.GetString("unlock_time");
            if (!int.TryParse(unlockTimeRaw, out var unlockAmount) || unlockAmount < 0)
                throw new InvalidDataException(
                    $"game_activity_stages row {id} has unlock_time '{unlockTimeRaw}', which is not a non-negative integer");

            var row = new SailingActivityStageRow
            {
                Id = id,
                Name = reader.GetString("stage_name"),
                StageNumber = reader.GetInt32("stage_number"),
                UnlockMode = reader.GetInt32("unlock_mode"),
                TimeMode = reader.GetInt32("time_mode"),
                UnlockTimeRaw = unlockTimeRaw,
                UnlockAmount = unlockAmount,
                PrerequisiteStageId = reader.GetInt32("prerequisite_stage_id"),
            };

            if (!_stages.TryAdd(id, row))
                throw new InvalidDataException($"game_activity_stages has duplicate id {id}");
        }

        // A prerequisite that names a stage which is not loaded would make the unlock chain
        // unreadable, so it is reported rather than followed.
        foreach (var stage in _stages.Values)
        {
            if (stage.PrerequisiteStageId != 0 && !_stages.ContainsKey(stage.PrerequisiteStageId))
                Diagnostics.AddOrphan("game_activity_stages", stage.Id, "game_activity_stages", stage.PrerequisiteStageId);
        }
    }

    private void LoadTasks(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, game_activity_stage_id, task_group_id, task_name, task_desc,
                   task_type, condition_type_id, rewards, reward_points, sort_order, is_point_reward
            FROM game_activity_tasks
            """;
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var id = reader.GetInt32("id");
            var stageId = reader.GetInt32("game_activity_stage_id");
            var rewardsRaw = reader.GetString("rewards");

            if (!PlotAuctionRules.TryParseRewards(rewardsRaw, out var rewards))
                throw new InvalidDataException($"game_activity_tasks row {id} has malformed rewards '{rewardsRaw}'");

            var row = new SailingActivityTaskRow
            {
                Id = id,
                StageId = stageId,
                TaskGroupId = reader.GetInt32("task_group_id"),
                Name = reader.GetString("task_name"),
                Description = reader.GetString("task_desc"),
                TaskType = reader.GetInt32("task_type"),
                ConditionTypeId = reader.GetInt32("condition_type_id"),
                RewardPoints = reader.GetInt32("reward_points"),
                IsPointReward = reader.GetBoolean("is_point_reward"),
                SortOrder = reader.GetInt32("sort_order"),
                Rewards = rewards,
            };

            // 0 is the column's own default and means "not bound to a stage". Anything else must
            // name a stage that exists; shipped content has references that do not.
            if (stageId != 0 && !_stages.ContainsKey(stageId))
                Diagnostics.AddOrphan("game_activity_tasks", id, "game_activity_stages", stageId);

            _tasks.Add(row);
        }

        _tasks.Sort((a, b) => a.SortOrder != b.SortOrder ? a.SortOrder.CompareTo(b.SortOrder) : a.Id.CompareTo(b.Id));
    }

    /// <summary>The activity with that id, or null when the catalog has no such row.</summary>
    public SailingActivityRow FindActivity(int activityId) =>
        _activities.GetValueOrDefault(activityId);

    /// <summary>The stage with that id, or null when the catalog has no such row.</summary>
    public SailingActivityStageRow FindStage(int stageId) =>
        _stages.GetValueOrDefault(stageId);

    /// <summary>
    /// The tasks a stage owns, in content order. Stage 0 is the table's unbound marker, so it is
    /// reachable here and is not an orphan.
    /// </summary>
    public IReadOnlyList<SailingActivityTaskRow> TasksForStage(int stageId) =>
        _tasks.Where(x => x.StageId == stageId).ToList();

    /// <summary>The tasks an activity selects through its authored task group.</summary>
    public IReadOnlyList<SailingActivityTaskRow> TasksForActivity(SailingActivityRow activity) =>
        activity is null || activity.TaskGroupId == 0
            ? []
            : _tasks.Where(x => x.TaskGroupId == activity.TaskGroupId).ToList();
}
