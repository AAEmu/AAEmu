using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.SailingActivity;
using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// The sailing activity's content loader against the real shipped column layout.
/// </summary>
/// <remarks>
/// The point of these tests is that shipped content is inconsistent and the loader has to survive
/// it: three tasks reference stages that do not exist, an activity's window is authored in a form
/// with no unit, and rewards cells are empty in some rows. Each of those must be reported rather
/// than thrown, while a genuinely malformed rewards cell must still stop the load.
/// </remarks>
[NotInParallel]
public class SailingActivityGameDataTests
{
    private static SqliteConnection CreateInMemory()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE game_activities (
                id INTEGER PRIMARY KEY,
                activity_name TEXT,
                activity_desc TEXT,
                status INTEGER DEFAULT 0,
                time_mode INTEGER DEFAULT 0,
                start_time TEXT,
                end_time TEXT,
                server_groups TEXT,
                task_group_id INTEGER DEFAULT 0
            );
            CREATE TABLE game_activity_stages (
                id INTEGER PRIMARY KEY,
                stage_name TEXT,
                stage_number INTEGER DEFAULT 0,
                unlock_mode INTEGER DEFAULT 0,
                time_mode INTEGER DEFAULT 0,
                unlock_time TEXT,
                prerequisite_stage_id INTEGER DEFAULT 0
            );
            CREATE TABLE game_activity_tasks (
                id INTEGER PRIMARY KEY,
                game_activity_stage_id INTEGER,
                task_group_id INTEGER,
                task_name TEXT,
                task_desc TEXT,
                task_type INTEGER,
                condition_type_id INTEGER,
                rewards TEXT,
                reward_points INTEGER,
                sort_order INTEGER,
                is_point_reward INTEGER DEFAULT 0
            );
            """;
        command.ExecuteNonQuery();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>The shipped shape: one resolvable activity, one activity with a bare-integer window,
    /// five stages, and the two tasks whose stage does not exist.</summary>
    private static SqliteConnection LoadShippedShape()
    {
        var connection = CreateInMemory();

        Execute(connection,
            """
            INSERT INTO game_activities (id, activity_name, status, time_mode, start_time, end_time, server_groups, task_group_id) VALUES
                (7, 'sailing', 1, 0, '2031|5|19|10|30', '2031|6|19|10|30', '1|2|3|4', 1),
                (8, 'festival', 0, 1, '0', '50', '1|2|3|4', 2);
            INSERT INTO game_activity_stages (id, stage_name, stage_number, unlock_mode, time_mode, unlock_time, prerequisite_stage_id) VALUES
                (40, 'stage one', 1, 3, 2, '0', 0),
                (50, 'stage two', 2, 3, 2, '1', 0),
                (60, 'stage three', 3, 3, 2, '5', 0),
                (70, 'stage four', 4, 3, 2, '10', 0),
                (80, 'stage five', 5, 3, 2, '15', 0);
            INSERT INTO game_activity_tasks
                (id, game_activity_stage_id, task_group_id, task_name, task_desc, task_type, condition_type_id, rewards, reward_points, sort_order, is_point_reward) VALUES
                (1, 11, 1, 'first sail', '', 1, 701, '', 5, 1, 0),
                (2, 11, 1, 'new island', '', 2, 702, '', 7, 2, 0),
                (3, 12, 1, 'defeat pirates', '', 3, 703, '', 9, 3, 0),
                (6, 0, 3, 'unbound', '', 1, 704, '', 9, 4, 0),
                (4, 40, 3, 'departure', '', 1, 705, '1|90001|2', 5, 1, 0),
                (5, 50, 3, 'edge', '', 2, 706, '1|90002|3', 5, 1, 0);
            """);

        SailingActivityGameData.Instance.Load(connection);
        return connection;
    }

    [Test]
    public async Task Load_ReadsEveryShippedRow()
    {
        using var connection = LoadShippedShape();
        var data = SailingActivityGameData.Instance;

        await Assert.That(data.Activities.Count).IsEqualTo(2);
        await Assert.That(data.Stages.Count).IsEqualTo(5);
        await Assert.That(data.Tasks.Count).IsEqualTo(6);
    }

    [Test]
    public async Task Load_OrdersStagesByStageNumberNotById()
    {
        using var connection = LoadShippedShape();

        await Assert.That(SailingActivityGameData.Instance.Stages.Select(x => x.StageNumber))
            .IsEquivalentTo(new[] { 1, 2, 3, 4, 5 });
    }

    [Test]
    public async Task Load_ReportsEveryOrphanStageReferenceAndKeepsGoing()
    {
        using var connection = LoadShippedShape();
        var data = SailingActivityGameData.Instance;

        // Tasks 1 and 2 point at stages 1 and 2, which the stage table does not contain. The loader
        // must not refuse to boot over that, and must name each one.
        await Assert.That(data.Diagnostics.Orphans.Count).IsEqualTo(3);
        await Assert.That(data.Diagnostics.Orphans.Select(x => x.RowId)).IsEquivalentTo(new[] { 1, 2, 3 });
        await Assert.That(data.Diagnostics.Orphans.Select(x => x.MissingId)).IsEquivalentTo(new[] { 11, 11, 12 });
        await Assert.That(data.Diagnostics.Orphans.All(x => x.MissingTable == "game_activity_stages")).IsTrue();

        // The rows themselves are still loaded, so nothing else has to be re-fetched.
        await Assert.That(data.Tasks.Count).IsEqualTo(6);
    }

    [Test]
    public async Task Load_TreatsStageZeroAsUnboundRatherThanOrphan()
    {
        using var connection = LoadShippedShape();
        var data = SailingActivityGameData.Instance;

        // The column defaults to 0 and every stage uses 0 for "no prerequisite", so a task with no
        // stage is a normal shipped state and must not be reported.
        await Assert.That(data.Diagnostics.Orphans.Any(x => x.RowId == 6)).IsFalse();
        await Assert.That(data.TasksForStage(0).Select(x => x.Id)).IsEquivalentTo(new[] { 6 });
    }

    [Test]
    public async Task Load_ResolvesTheAbsoluteWindowAndRefusesTheBareIntegerOne()
    {
        using var connection = LoadShippedShape();
        var data = SailingActivityGameData.Instance;

        var sailing = data.FindActivity(7);
        await Assert.That(sailing).IsNotNull();
        await Assert.That(sailing.StartUtc.HasValue).IsTrue();
        await Assert.That(sailing.EndUtc.HasValue).IsTrue();
        await Assert.That(sailing.StartUtc!.Value.Year).IsEqualTo(2031);
        await Assert.That(sailing.EndUtc!.Value > sailing.StartUtc!.Value).IsTrue();

        // '0'/'50' states no unit and no reference point, so it stays unresolved rather than being
        // turned into an instant this slice invented.
        var festival = data.FindActivity(8);
        await Assert.That(festival.StartUtc.HasValue).IsFalse();
        await Assert.That(festival.EndUtc.HasValue).IsFalse();
        await Assert.That(data.Diagnostics.UnresolvedWindows.Count).IsEqualTo(1);
        await Assert.That(data.Diagnostics.UnresolvedWindows[0].RowId).IsEqualTo(8);
    }

    [Test]
    public async Task Load_KeepsTheAuthoredUnlockValueWithoutInferringAUnit()
    {
        using var connection = LoadShippedShape();
        var data = SailingActivityGameData.Instance;

        var first = data.FindStage(40);
        await Assert.That(first).IsNotNull();
        await Assert.That(first.UnlockTimeRaw).IsEqualTo("0");
        await Assert.That(first.UnlockAmount).IsEqualTo(0);

        var last = data.FindStage(80);
        await Assert.That(last!.UnlockAmount).IsEqualTo(15);

        // No instant is derived from the unlock value anywhere on the row: what the number counts is
        // not recovered, so there is deliberately no DateTime on the type at all.
        var instantProperties = typeof(SailingActivityStageRow)
            .GetProperties()
            .Where(x => x.PropertyType == typeof(DateTime) || x.PropertyType == typeof(DateTime?))
            .Select(x => x.Name)
            .ToList();
        await Assert.That(instantProperties).IsEmpty();
    }

    [Test]
    public async Task Load_ParsesTheRewardsCellAndAcceptsAnEmptyOne()
    {
        using var connection = LoadShippedShape();
        var data = SailingActivityGameData.Instance;

        var withReward = data.Tasks.First(x => x.Id == 4);
        await Assert.That(withReward.Rewards.Count).IsEqualTo(1);
        await Assert.That(withReward.Rewards[0].ItemId).IsEqualTo(90001u);
        await Assert.That(withReward.Rewards[0].Count).IsEqualTo(2u);

        // An empty rewards cell is legal shipped content. What it means for a given task is not
        // asserted here; the point is only that the loader accepts the cell rather than refusing it.
        var empty = data.Tasks.First(x => x.Id == 1);
        await Assert.That(empty.Rewards).IsEmpty();
    }

    [Test]
    public async Task Load_KeepsTheAuthoredPointAndKindValuesWithoutAWhitelist()
    {
        using var connection = LoadShippedShape();
        var data = SailingActivityGameData.Instance;

        // The shipped tables carry condition and task kinds wider than any documented set. They load
        // verbatim; nothing here decides what they mean.
        await Assert.That(data.Tasks.Count).IsEqualTo(6);
        await Assert.That(data.Tasks.Where(x => x.Id is 1 or 2 or 3 or 6).Select(x => x.ConditionTypeId))
            .IsEquivalentTo(new[] { 701, 702, 703, 704 });
        await Assert.That(data.Tasks.Where(x => x.Id is 1 or 2 or 3 or 6).Select(x => x.TaskType))
            .IsEquivalentTo(new[] { 1, 2, 3, 1 });
        await Assert.That(data.Tasks.First(x => x.Id == 1).RewardPoints).IsEqualTo(5);
    }

    [Test]
    public async Task Load_SelectsAnActivitysTasksThroughItsAuthoredGroup()
    {
        using var connection = LoadShippedShape();
        var data = SailingActivityGameData.Instance;

        // The sailing activity selects group 1, and in shipped content every task in that group
        // names a stage that does not exist. The selection is still exact; it just does not reach a
        // stage. Reported rather than silently repaired.
        var sailing = data.FindActivity(7);
        await Assert.That(sailing!.TaskGroupId).IsEqualTo(1);
        await Assert.That(data.TasksForActivity(sailing).Select(x => x.Id)).IsEquivalentTo(new[] { 1, 2, 3 });
        await Assert.That(data.TasksForActivity(sailing).Where(x => data.FindStage(x.StageId) is not null)).IsEmpty();

        // Group 3 is the one the stages actually own.
        await Assert.That(data.TasksForStage(50).Select(x => x.Id)).IsEquivalentTo(new[] { 5 });
    }

    [Test]
    public async Task Load_OrdersTasksBySortOrderThenId()
    {
        using var connection = CreateInMemory();
        Execute(connection,
            """
            INSERT INTO game_activity_stages (id, stage_name, stage_number, unlock_mode, time_mode, unlock_time, prerequisite_stage_id) VALUES
                (40, 'only', 1, 3, 2, '0', 0);
            INSERT INTO game_activity_tasks
                (id, game_activity_stage_id, task_group_id, task_name, task_desc, task_type, condition_type_id, rewards, reward_points, sort_order, is_point_reward) VALUES
                (10, 40, 1, 'second', '', 1, 705, '', 5, 2, 0),
                (11, 40, 1, 'first', '', 1, 705, '', 5, 1, 0),
                (12, 40, 1, 'also first', '', 1, 705, '', 5, 1, 0);
            """);
        SailingActivityGameData.Instance.Load(connection);

        await Assert.That(SailingActivityGameData.Instance.Tasks.Select(x => x.Id))
            .IsEquivalentTo(new[] { 11, 12, 10 });
    }

    [Test]
    public async Task Load_ReportsAStageWhosePrerequisiteIsNotLoaded()
    {
        using var connection = CreateInMemory();
        Execute(connection,
            """
            INSERT INTO game_activity_stages (id, stage_name, stage_number, unlock_mode, time_mode, unlock_time, prerequisite_stage_id) VALUES
                (40, 'one', 1, 3, 2, '0', 0),
                (50, 'two', 2, 3, 2, '1', 77);
            """);
        SailingActivityGameData.Instance.Load(connection);

        var orphans = SailingActivityGameData.Instance.Diagnostics.Orphans;
        await Assert.That(orphans.Count).IsEqualTo(1);
        await Assert.That(orphans[0].RowId).IsEqualTo(50);
        await Assert.That(orphans[0].MissingId).IsEqualTo(77);
    }

    [Test]
    public async Task Load_RefusesAMalformedRewardsCell()
    {
        using var connection = CreateInMemory();
        Execute(connection,
            """
            INSERT INTO game_activity_stages (id, stage_name, stage_number, unlock_mode, time_mode, unlock_time, prerequisite_stage_id) VALUES
                (40, 'only', 1, 3, 2, '0', 0);
            INSERT INTO game_activity_tasks
                (id, game_activity_stage_id, task_group_id, task_name, task_desc, task_type, condition_type_id, rewards, reward_points, sort_order, is_point_reward) VALUES
                (1, 40, 1, 'broken', '', 1, 705, '1|90001', 5, 1, 0);
            """);

        await Assert.That(() => SailingActivityGameData.Instance.Load(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task Load_RefusesADuplicateActivityId()
    {
        // The shipped table declares id as a primary key, so SQLite would reject a duplicate before
        // the loader ever saw it. The table below drops the key so the loader's own guard is what
        // is under test.
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        Execute(connection,
            """
            CREATE TABLE game_activities (
                id INTEGER, activity_name TEXT, activity_desc TEXT, status INTEGER DEFAULT 0,
                time_mode INTEGER DEFAULT 0, start_time TEXT, end_time TEXT,
                server_groups TEXT, task_group_id INTEGER DEFAULT 0
            );
            CREATE TABLE game_activity_stages (
                id INTEGER, stage_name TEXT, stage_number INTEGER DEFAULT 0, unlock_mode INTEGER DEFAULT 0,
                time_mode INTEGER DEFAULT 0, unlock_time TEXT, prerequisite_stage_id INTEGER DEFAULT 0
            );
            CREATE TABLE game_activity_tasks (
                id INTEGER, game_activity_stage_id INTEGER, task_group_id INTEGER, task_name TEXT,
                task_desc TEXT, task_type INTEGER, condition_type_id INTEGER, rewards TEXT,
                reward_points INTEGER, sort_order INTEGER, is_point_reward INTEGER DEFAULT 0
            );
            INSERT INTO game_activities (id, activity_name, status, time_mode, start_time, end_time, server_groups, task_group_id) VALUES
                (7, 'first', 1, 0, '2031|1|1|0|0', '2031|2|1|0|0', '0', 0),
                (7, 'second', 1, 0, '2031|1|1|0|0', '2031|2|1|0|0', '0', 0);
            """);

        await Assert.That(() => SailingActivityGameData.Instance.Load(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task Load_RefusesAWindowThatEndsBeforeItStarts()
    {
        using var connection = CreateInMemory();
        Execute(connection,
            """
            INSERT INTO game_activities (id, activity_name, status, time_mode, start_time, end_time, server_groups, task_group_id) VALUES
                (7, 'backwards', 1, 0, '2031|6|1|0|0', '2031|5|1|0|0', '0', 0);
            """);
        SailingActivityGameData.Instance.Load(connection);

        await Assert.That(SailingActivityGameData.Instance.FindActivity(7)!.StartUtc.HasValue).IsFalse();
        await Assert.That(SailingActivityGameData.Instance.Diagnostics.UnresolvedWindows.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Load_RefusesAnUnlockValueThatIsNotANumber()
    {
        using var connection = CreateInMemory();
        Execute(connection,
            """
            INSERT INTO game_activity_stages (id, stage_name, stage_number, unlock_mode, time_mode, unlock_time, prerequisite_stage_id) VALUES
                (40, 'broken', 1, 3, 2, 'whenever', 0);
            """);

        // Silently nulling this would make the stage unlock never fire without anything saying so.
        await Assert.That(() => SailingActivityGameData.Instance.Load(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task Load_RefusesAnEmptyUnlockValue()
    {
        using var connection = CreateInMemory();
        Execute(connection,
            """
            INSERT INTO game_activity_stages (id, stage_name, stage_number, unlock_mode, time_mode, unlock_time, prerequisite_stage_id) VALUES
                (40, 'empty', 1, 3, 2, '', 0);
            """);

        await Assert.That(() => SailingActivityGameData.Instance.Load(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task Load_RefusesANegativeUnlockValue()
    {
        using var connection = CreateInMemory();
        Execute(connection,
            """
            INSERT INTO game_activity_stages (id, stage_name, stage_number, unlock_mode, time_mode, unlock_time, prerequisite_stage_id) VALUES
                (40, 'negative', 1, 3, 2, '-1', 0);
            """);

        await Assert.That(() => SailingActivityGameData.Instance.Load(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task Load_AcceptsAZeroUnlockValueBecauseItIsWhatShips()
    {
        using var connection = CreateInMemory();
        Execute(connection,
            """
            INSERT INTO game_activity_stages (id, stage_name, stage_number, unlock_mode, time_mode, unlock_time, prerequisite_stage_id) VALUES
                (40, 'zero', 1, 3, 2, '0', 0);
            """);
        SailingActivityGameData.Instance.Load(connection);

        await Assert.That(SailingActivityGameData.Instance.FindStage(40)!.UnlockAmount).IsEqualTo(0);
    }
}
