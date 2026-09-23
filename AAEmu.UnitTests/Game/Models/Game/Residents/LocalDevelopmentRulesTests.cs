using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Residents;

using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Models.Game.Residents;

/// <summary>
/// The local-development ladder: board thresholds parsed from show_text, distinct thresholds
/// crossed -> development level -> doodad/board func-group phases, and the loader's loud skip of
/// board rows that announce no threshold.
/// </summary>
public class LocalDevelopmentRulesTests
{
    private static LocalDevelopmentDefinition Definition(int[] phases, params (uint RowId, uint ShowPhase, string Text)[] rows)
    {
        var definition = new LocalDevelopmentDefinition
        {
            Id = 32,
            ZoneGroupId = 102,
            DoodadAlmightyId = 11590,
            BoardDoodadId = 13600,
            DoodadPhases = phases,
        };
        foreach (var (rowId, showPhase, text) in rows)
        {
            definition.BoardRows.Add(new LocalDevelopmentBoardRow(
                rowId,
                5,
                showPhase,
                LocalDevelopmentRules.ParseThreshold(text)));
        }
        return definition;
    }

    [Test]
    public async Task ParseThreshold_ReadsTheAsciiDigitRunOutOfShowText()
    {
        // The digit run is the locale-invariant part of the notice; the prose around it is not.
        await Assert.That(LocalDevelopmentRules.ParseThreshold("Zone: 60")).IsEqualTo((uint?)60);
        await Assert.That(LocalDevelopmentRules.ParseThreshold("Woods: 100 units")).IsEqualTo((uint?)100);
        await Assert.That(LocalDevelopmentRules.ParseThreshold("잡동사니 7개")).IsEqualTo((uint?)7);
        await Assert.That(LocalDevelopmentRules.ParseThreshold("no digits here")).IsNull();
        await Assert.That(LocalDevelopmentRules.ParseThreshold(string.Empty)).IsNull();
    }

    [Test]
    public async Task Evaluate_MapsContributionOntoTheBoardThresholdLadder()
    {
        var definition = Definition([1000, 1100, 1200, 1300], (1, 5000, "Woods: 60"), (2, 5001, "Woods: 100"));

        var belowFirst = LocalDevelopmentRules.Evaluate(definition, 59);
        await Assert.That(belowFirst.Level).IsEqualTo(0u);
        await Assert.That(belowFirst.DoodadPhase).IsEqualTo((uint?)1000);
        await Assert.That(belowFirst.BoardPhase).IsNull();

        var atFirst = LocalDevelopmentRules.Evaluate(definition, 60);
        await Assert.That(atFirst.Level).IsEqualTo(1u);
        await Assert.That(atFirst.DoodadPhase).IsEqualTo((uint?)1100);
        await Assert.That(atFirst.BoardPhase).IsEqualTo((uint?)5000);

        var atSecond = LocalDevelopmentRules.Evaluate(definition, 100);
        await Assert.That(atSecond.Level).IsEqualTo(2u);
        await Assert.That(atSecond.DoodadPhase).IsEqualTo((uint?)1200);
        await Assert.That(atSecond.BoardPhase).IsEqualTo((uint?)5001);

        // Two thresholds cap the contribution ladder at level 2: doodad_phase_3 has its own
        // (unresolved) trigger in the content and is never reached from board thresholds alone.
        var wayAbove = LocalDevelopmentRules.Evaluate(definition, 10_000);
        await Assert.That(wayAbove.Level).IsEqualTo(2u);
        await Assert.That(wayAbove.DoodadPhase).IsEqualTo((uint?)1200);
        await Assert.That(wayAbove.BoardPhase).IsEqualTo((uint?)5001);
    }

    [Test]
    public async Task Evaluate_CountsADuplicatedThresholdOnce()
    {
        // Two board types can announce the same count (pockets 20 / junk 20): the level counts
        // DISTINCT thresholds, so {7, 20, 60} is three levels, not four rows.
        var definition = Definition(
            [1000, 1100, 1200, 1300],
            (1, 6000, "pockets: 7"),
            (2, 6001, "pockets: 20"),
            (3, 6002, "junk: 20"),
            (4, 6003, "junk: 60"));

        var atTwenty = LocalDevelopmentRules.Evaluate(definition, 20);
        await Assert.That(atTwenty.Level).IsEqualTo(2u);
        await Assert.That(atTwenty.DoodadPhase).IsEqualTo((uint?)1200);
        // Board tie at threshold 20 is broken by board row id (table order): row 3 wins.
        await Assert.That(atTwenty.BoardPhase).IsEqualTo((uint?)6002);

        var atSixty = LocalDevelopmentRules.Evaluate(definition, 60);
        await Assert.That(atSixty.Level).IsEqualTo(3u);
        await Assert.That(atSixty.DoodadPhase).IsEqualTo((uint?)1300);
        await Assert.That(atSixty.BoardPhase).IsEqualTo((uint?)6003);
    }

    [Test]
    public async Task PhaseForLevel_IsNullWhenTheContentLeftThePhaseUndefined()
    {
        var definition = Definition([-1, 1100, -1, -1]);

        // -1 is the content default for "no phase here": loud skip, never a fallback number.
        await Assert.That(LocalDevelopmentRules.PhaseForLevel(definition, 0)).IsNull();
        await Assert.That(LocalDevelopmentRules.PhaseForLevel(definition, 1)).IsEqualTo((uint?)1100);
        await Assert.That(LocalDevelopmentRules.PhaseForLevel(definition, 2)).IsNull();
        // A level beyond the ladder has no phase either.
        await Assert.That(LocalDevelopmentRules.PhaseForLevel(definition, 4)).IsNull();
    }

    [Test]
    public async Task ShouldChangePhase_OnlyWhenTheSpawnedDoodadIsElsewhere()
    {
        await Assert.That(LocalDevelopmentRules.ShouldChangePhase(40000, 40000)).IsFalse();
        await Assert.That(LocalDevelopmentRules.ShouldChangePhase(40000, 40001)).IsTrue();
    }

    [Test]
    public async Task Load_ReadsDevelopmentsAndKeepsBoardNoticesWithoutAThreshold()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE local_developments (
                    id INTEGER PRIMARY KEY,
                    zone_group_id INTEGER NOT NULL,
                    doodad_almighty_id INTEGER NOT NULL,
                    doodad_phase_0 INTEGER NOT NULL DEFAULT -1,
                    doodad_phase_1 INTEGER NOT NULL DEFAULT -1,
                    doodad_phase_2 INTEGER NOT NULL DEFAULT -1,
                    doodad_phase_3 INTEGER NOT NULL DEFAULT -1,
                    board_doodad_id INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE local_development_boards (
                    id INTEGER PRIMARY KEY,
                    local_development_id INTEGER NOT NULL,
                    local_development_board_type_id INTEGER NOT NULL,
                    show_phase INTEGER NOT NULL,
                    show_text TEXT NOT NULL);
                INSERT INTO local_developments VALUES (32, 102, 11590, 40000, 40001, 40002, 40003, 13600);
                INSERT INTO local_development_boards (id, local_development_id, local_development_board_type_id, show_phase, show_text)
                    VALUES (1, 32, 5, 47635, 'Sea of Innocence: pouch 20'),
                           (2, 32, 5, 47636, 'no threshold announced');
                """;
            command.ExecuteNonQuery();
        }

        var loader = new LocalDevelopmentGameData();
        loader.Load(connection);

        var definition = loader.GetByZoneGroup(102);
        await Assert.That(definition).IsNotNull();
        await Assert.That(definition.Id).IsEqualTo(32u);
        await Assert.That(definition.DoodadAlmightyId).IsEqualTo(11590u);
        await Assert.That(definition.BoardDoodadId).IsEqualTo(13600u);
        await Assert.That(definition.DoodadPhases[1]).IsEqualTo(40001);
        await Assert.That(definition.BoardRows.Count).IsEqualTo(2);
        await Assert.That(definition.BoardRows[0].ShowPhase).IsEqualTo(47635u);
        await Assert.That(definition.BoardRows[0].Threshold).IsNull();
        await Assert.That(definition.BoardRows[1].Threshold).IsNull();

        // A zone group with no local_developments row resolves to null: the state machine
        // skips loudly for it instead of guessing.
        await Assert.That(loader.GetByZoneGroup(9999)).IsNull();
    }
}
