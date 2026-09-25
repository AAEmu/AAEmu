using System.Reflection;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

/// <summary>
/// The self-dispatch decision rests on one data fact: a NULL <c>npc_id</c> column must
/// load as the loader's no-value. This runs the production reader over a real SQLite
/// database with the shipped table shape, so the contract is pinned at the data layer
/// rather than only in a hand-built object.
/// </summary>
public class DoodadAreaTriggerContentTests
{
    [Test]
    public async Task ANullNpcIdColumnLoadsAsNoValueAndDispatchesSelf()
    {
        await using var db = CreateDatabase();
        Seed(db, id: 1714, npcId: null, isEnter: true);

        var template = LoadTemplates(db).Single();

        // The loader's default for a NULL column is 0, which is what ResolveTarget reads.
        await Assert.That(template.NpcId).IsEqualTo(0u);
        await Assert.That(template.IsEnter).IsTrue();
        await Assert.That(DoodadAreaTriggerRuntime.ResolveTarget(template))
            .IsEqualTo(AreaTriggerTarget.Self);
        await Assert.That(DoodadAreaTriggerRuntime.ShouldDispatch(template, entering: true)).IsTrue();
    }

    [Test]
    public async Task APopulatedNpcIdColumnLoadsAsItsValueAndIsRefused()
    {
        await using var db = CreateDatabase();
        Seed(db, id: 1715, npcId: 4242, isEnter: true);

        var template = LoadTemplates(db).Single();

        await Assert.That(template.NpcId).IsEqualTo(4242u);
        await Assert.That(DoodadAreaTriggerRuntime.ResolveTarget(template))
            .IsEqualTo(AreaTriggerTarget.Unmapped);
        await Assert.That(DoodadAreaTriggerRuntime.ShouldDispatch(template, entering: true)).IsFalse();
    }

    [Test]
    public async Task ALeaveRowLoadsButNeverMatchesAnEnterEdge()
    {
        await using var db = CreateDatabase();
        Seed(db, id: 1716, npcId: null, isEnter: false);

        var template = LoadTemplates(db).Single();

        await Assert.That(template.IsEnter).IsFalse();
        await Assert.That(DoodadAreaTriggerRuntime.ShouldDispatch(template, entering: true)).IsFalse();
        await Assert.That(DoodadAreaTriggerRuntime.ShouldDispatch(template, entering: false)).IsTrue();
    }

    /// <summary>
    /// A throwaway database holding only the table this feature reads, built with the
    /// shipped column shape. Lives in the system temp directory, never in the repo.
    /// </summary>
    private static SqliteConnection CreateDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"aaemu-q11-{Guid.NewGuid():N}.sqlite3");
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "CREATE TABLE doodad_func_area_triggers (id INTEGER NOT NULL, npc_id INTEGER NULL, is_enter INTEGER NULL)";
        command.ExecuteNonQuery();
        return connection;
    }

    private static void Seed(SqliteConnection connection, int id, int? npcId, bool isEnter)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO doodad_func_area_triggers (id, npc_id, is_enter) VALUES ($id, $npc, $enter)";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$npc", (object?)npcId ?? DBNull.Value);
        command.Parameters.AddWithValue("$enter", isEnter ? 1 : 0);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Reads the rows exactly the way the content loader does: the same reader type and
    /// the same column reads, so a change to either side breaks this test.
    /// </summary>
    private static List<DoodadFuncAreaTrigger> LoadTemplates(SqliteConnection connection)
    {
        var templates = new List<DoodadFuncAreaTrigger>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM doodad_func_area_triggers";
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            templates.Add(new DoodadFuncAreaTrigger
            {
                Id = reader.GetUInt32("id"),
                NpcId = reader.GetUInt32("npc_id", 0),
                IsEnter = reader.GetBoolean("is_enter", true),
            });
        }

        return templates;
    }
}
