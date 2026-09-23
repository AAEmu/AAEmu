using AAEmu.Game.GameData;

namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// <c>instrument_sounds</c> decides what this server treats as an instrument, so a row it cannot
/// answer for has to be reported rather than guessed at: a kind the enum does not name, or a buff
/// content does not carry, drops the row and is listed for the load's one loud line.
/// </summary>
// Both test classes that load InstrumentSoundGameData.Instance must not run alongside each other.
[NotInParallel]
public class InstrumentSoundGameDataTests : SqliteTestBase
{
    protected override void CreateTestSchema()
    {
        base.CreateTestSchema();
        using (var command = Connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE enum_instrument_sound_kinds (
                    id INTEGER PRIMARY KEY,
                    name TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        using (var command = Connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE buffs (
                    id INTEGER PRIMARY KEY,
                    name TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        using (var command = Connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE instrument_sounds (
                    id INTEGER PRIMARY KEY,
                    item_id INTEGER NOT NULL,
                    midi INTEGER NOT NULL,
                    kind_id INTEGER NOT NULL,
                    buff_id INTEGER
                );
                """;
            command.ExecuteNonQuery();
        }
    }

    private void Execute(string sql)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Test]
    public async Task Rows_LoadThroughTheKindNamesTheirEnumGives()
    {
        // 1='item' and 2='doodad' are what the shipped enum names them; the ids themselves are not.
        Execute("INSERT INTO enum_instrument_sound_kinds (id, name) VALUES (1, 'item'), (2, 'doodad')");
        Execute("INSERT INTO buffs (id, name) VALUES (91001, 'piano play')");
        Execute("INSERT INTO instrument_sounds (id, item_id, midi, kind_id, buff_id) VALUES " +
                "(1, 90002, 24, 1, NULL), " + // an instrument item carrying no buff of its own
                "(2, 90001, 0, 2, 91001)"); // the placed piano

        InstrumentSoundGameData.Instance.Load(Connection);

        await Assert.That(InstrumentSoundGameData.Instance.TryGetItem(90002, out var item)).IsTrue();
        await Assert.That(item.Midi).IsEqualTo(24u);
        await Assert.That(item.BuffId).IsEqualTo(0u);

        await Assert.That(InstrumentSoundGameData.Instance.TryGetDoodad(90001, out var doodad)).IsTrue();
        await Assert.That(doodad.BuffId).IsEqualTo(91001u);

        await Assert.That(InstrumentSoundGameData.Instance.UnknownKindRowIds).IsEmpty();
        await Assert.That(InstrumentSoundGameData.Instance.UnknownBuffIds).IsEmpty();
        await Assert.That(InstrumentSoundGameData.Instance.KindKeys)
            .IsEquivalentTo(new List<string> { "item", "doodad" });
    }

    [Test]
    public async Task AKindTheEnumDoesNotName_DropsTheRowAndIsReported()
    {
        Execute("INSERT INTO enum_instrument_sound_kinds (id, name) VALUES (1, 'item')");
        Execute("INSERT INTO instrument_sounds (id, item_id, midi, kind_id, buff_id) VALUES (7, 90003, 5, 9, NULL)");

        InstrumentSoundGameData.Instance.Load(Connection);

        // Loud: the row id is listed, and nothing is registered for it under either key.
        await Assert.That(InstrumentSoundGameData.Instance.UnknownKindRowIds).IsEquivalentTo(new List<uint> { 7 });
        await Assert.That(InstrumentSoundGameData.Instance.TryGetItem(90003, out _)).IsFalse();
        await Assert.That(InstrumentSoundGameData.Instance.TryGetDoodad(90003, out _)).IsFalse();

        var warning = InstrumentSoundLoadRules.Warning(
            InstrumentSoundGameData.Instance.UnknownKindRowIds, InstrumentSoundGameData.Instance.UnknownBuffIds);
        await Assert.That(warning).Contains("enum_instrument_sound_kinds");
        await Assert.That(warning).Contains("7");
    }

    [Test]
    public async Task ABuffContentDoesNotCarry_DropsTheRowAndIsReported()
    {
        // The row names a buff that does not exist: applying it would silently do nothing, and
        // there is no fallback id to put in its place, so the whole row goes.
        Execute("INSERT INTO enum_instrument_sound_kinds (id, name) VALUES (2, 'doodad')");
        Execute("INSERT INTO instrument_sounds (id, item_id, midi, kind_id, buff_id) VALUES (8, 90004, 0, 2, 999999)");

        InstrumentSoundGameData.Instance.Load(Connection);

        await Assert.That(InstrumentSoundGameData.Instance.UnknownBuffIds).IsEquivalentTo(new List<uint> { 999999 });
        await Assert.That(InstrumentSoundGameData.Instance.TryGetDoodad(90004, out _)).IsFalse();

        var warning = InstrumentSoundLoadRules.Warning(
            InstrumentSoundGameData.Instance.UnknownKindRowIds, InstrumentSoundGameData.Instance.UnknownBuffIds);
        await Assert.That(warning).Contains("999999");
        await Assert.That(warning).Contains("no buffs row");
    }
}
