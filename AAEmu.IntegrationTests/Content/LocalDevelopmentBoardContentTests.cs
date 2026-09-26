using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AAEmu.IntegrationTests.Content;

/// <summary>
/// The local-development board slice against the shipped compact database, not a hand-built fixture:
/// every descriptor has to resolve to a real doodad function of this type, every board type has to
/// resolve to a real catalog row, and every board row has to carry a permission this feature models.
/// </summary>
/// <remarks>
/// Gated on <see cref="CompactDatabaseLocator.EnvVar"/> (or the app's deployment default). A runner
/// with no game content skips with that message instead of failing; a runner with the content runs
/// every assertion below against the shipped rows, so the gate cannot hide a content regression.
/// The placement test additionally gates on <see cref="ZoneLevelPlacements.IsConfigured"/>.
/// </remarks>
public sealed class LocalDevelopmentBoardContentTests
{
    private const string FuncType = nameof(DoodadFuncLocalDevelopmentBoardUiOpen);

    private static SqliteConnection OpenCompact()
    {
        Assert.SkipUnless(CompactDatabaseLocator.ResolvePath() is not null, CompactDatabaseLocator.SkipMessage);
        return CompactDatabaseLocator.Open();
    }

    [Fact]
    public void EveryDescriptorJoinsExactlyOneDoodadFuncOfThisType()
    {
        using var db = OpenCompact();
        var rows = CompactDatabaseLocator.Query(db, $"""
            SELECT d.id, COUNT(f.id) AS func_rows, MIN(f.actual_func_type) AS func_type
            FROM doodad_func_local_development_board_ui_opens d
            LEFT JOIN doodad_funcs f ON f.actual_func_id = d.id AND f.actual_func_type = '{FuncType}'
            GROUP BY d.id
            """);

        Assert.NotEmpty(rows);
        // A descriptor that joins nothing, or joins twice, is a content split the loader cannot express.
        Assert.All(rows, r => Assert.Equal(1, Convert.ToInt32(r[1])));
        Assert.All(rows, r => Assert.Equal(FuncType, (string)r[2]));

        var ids = rows.Select(r => Convert.ToUInt32(r[0])).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void NoDoodadFuncOfThisTypeIsMissingItsDescriptor()
    {
        // The reverse join catches a function row pointing at an id no descriptor defines: the
        // interaction would resolve a template with no board type and open nothing.
        using var db = OpenCompact();
        var orphans = CompactDatabaseLocator.Query(db, $"""
            SELECT f.actual_func_id
            FROM doodad_funcs f
            LEFT JOIN doodad_func_local_development_board_ui_opens d ON d.id = f.actual_func_id
            WHERE f.actual_func_type = '{FuncType}' AND d.id IS NULL
            """);

        Assert.Empty(orphans);
    }

    [Fact]
    public void EveryBoardRowIsPublic()
    {
        // The handler opens only the public row and refuses anything else, so a board shipping another
        // permission is a behaviour this feature would drop on the floor rather than model.
        using var db = OpenCompact();
        var rows = CompactDatabaseLocator.Query(db, $"""
            SELECT DISTINCT f.perm_id, e.name
            FROM doodad_funcs f
            JOIN doodad_func_local_development_board_ui_opens d ON d.id = f.actual_func_id
            LEFT JOIN enum_doodad_perms e ON e.id = f.perm_id
            WHERE f.actual_func_type = '{FuncType}'
            """);

        var row = Assert.Single(rows);
        Assert.Equal((uint)DoodadFuncPermission.Public, Convert.ToUInt32(row[0]));
        // The catalog spells the permission its own way; compare the value, not the label's spelling.
        Assert.Equal(nameof(DoodadFuncPermission.Public).ToUpperInvariant(), (string)row[1]);
    }

    [Fact]
    public void EveryBoardTypeResolvesInTheCatalog()
    {
        using var db = OpenCompact();
        var orphans = CompactDatabaseLocator.Query(db, """
            SELECT d.id, d.local_development_board_type_id
            FROM doodad_func_local_development_board_ui_opens d
            LEFT JOIN local_development_board_types t ON t.id = d.local_development_board_type_id
            WHERE t.id IS NULL
            """);

        Assert.Empty(orphans);
    }

    [Fact]
    public void EveryLocalDevelopmentBoardCarriesADescriptor()
    {
        using var db = OpenCompact();
        var missing = CompactDatabaseLocator.Query(db, $"""
            SELECT l.board_doodad_id
            FROM local_developments l
            WHERE NOT EXISTS (
                SELECT 1 FROM doodad_funcs f
                JOIN doodad_func_groups g ON g.id = f.doodad_func_group_id
                WHERE g.doodad_almighty_id = l.board_doodad_id AND f.actual_func_type = '{FuncType}')
            """);

        Assert.Empty(missing);
    }

    [Fact]
    public void TheOnlyBoardWithoutADevelopmentIsTheShippedTestBoard()
    {
        // One template carries the descriptor without being a local development. It is a shipped test
        // board, so the level pack must treat it as a board to ignore rather than a development that
        // failed to load. What identifies it is its shape, not its name: it is the only orphan, and it
        // covers several board types at once instead of one development's single board.
        using var db = OpenCompact();
        var testBoard = Assert.Single(BoardTemplatesWithoutDevelopment(db));

        var boardTypes = CompactDatabaseLocator.Query(db, $"""
            SELECT DISTINCT d.local_development_board_type_id
            FROM doodad_funcs f
            JOIN doodad_func_local_development_board_ui_opens d ON d.id = f.actual_func_id
            JOIN doodad_func_groups g ON g.id = f.doodad_func_group_id
            WHERE f.actual_func_type = '{FuncType}' AND g.doodad_almighty_id = @id
            """, ("@id", testBoard));

        Assert.True(boardTypes.Count > 1, "The test board should carry more than one board type.");
    }

    [Fact]
    public void TheTestBoardIsAlsoTheOnlyBoardTheLevelPackCannotPlace()
    {
        // The board templates are what the level pack plants, so a board with no cell placement is
        // skipped rather than invented. The test board is that case today; the real boards are not.
        // Both halves are asserted together on purpose: the first fails when the level files are not
        // configured, so "no placement" can never pass just because nothing was read.
        Assert.SkipUnless(ZoneLevelPlacements.IsConfigured, ZoneLevelPlacements.SkipMessage);
        using var db = OpenCompact();
        var testBoard = BoardTemplatesWithoutDevelopment(db);
        var developmentBoards = CompactDatabaseLocator.Query(db, "SELECT DISTINCT board_doodad_id FROM local_developments")
            .Select(r => Convert.ToUInt32(r[0])).ToList();

        Assert.True(ZoneLevelPlacements.Count(developmentBoards) > 0,
            "The local development boards should have level placements.");
        Assert.Equal(0, ZoneLevelPlacements.Count(testBoard));
    }

    private static List<uint> BoardTemplatesWithoutDevelopment(SqliteConnection db)
    {
        return CompactDatabaseLocator.Query(db, $"""
            SELECT DISTINCT g.doodad_almighty_id
            FROM doodad_funcs f
            JOIN doodad_func_groups g ON g.id = f.doodad_func_group_id
            WHERE f.actual_func_type = '{FuncType}'
              AND g.doodad_almighty_id NOT IN (SELECT board_doodad_id FROM local_developments)
            """).Select(r => Convert.ToUInt32(r[0])).ToList();
    }
}
