using System.Collections.Concurrent;

using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Residents;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.GameData;

/// <summary>
/// <c>local_developments</c> (one row per zone group's development: the almighty/board doodad ids
/// and the <c>doodad_phase_0..3</c> ladder) joined with <c>local_development_boards</c> (the board
/// notices whose <c>show_text</c> carries the contribution threshold as an ASCII digit-run).
/// </summary>
/// <remarks>
/// Only ids, phases and parsed thresholds are loaded. <c>phase_effect_*</c> is locale display text,
/// <c>balance_phase</c>/<c>hunting_charge</c>/<c>reward_group</c> triggers are not modelled — their
/// rules are unresolved for 10.0.2.13 and nothing here guesses at them.
/// </remarks>
[GameData]
public class LocalDevelopmentGameData : Singleton<LocalDevelopmentGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly ConcurrentDictionary<ushort, LocalDevelopmentDefinition> _byZoneGroup = new();
    private readonly ConcurrentDictionary<uint, LocalDevelopmentDefinition> _byDevelopmentId = new();

    public void Load(SqliteConnection connection)
    {
        _byZoneGroup.Clear();
        _byDevelopmentId.Clear();

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT id, zone_group_id, doodad_almighty_id,
                       doodad_phase_0, doodad_phase_1, doodad_phase_2, doodad_phase_3,
                       board_doodad_id
                FROM local_developments
                """;
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var definition = new LocalDevelopmentDefinition
                {
                    Id = reader.GetUInt32("id"),
                    ZoneGroupId = (ushort)reader.GetUInt32("zone_group_id"),
                    DoodadAlmightyId = reader.GetUInt32("doodad_almighty_id"),
                    BoardDoodadId = reader.GetUInt32("board_doodad_id"),
                    DoodadPhases =
                    [
                        reader.GetInt32("doodad_phase_0", -1),
                        reader.GetInt32("doodad_phase_1", -1),
                        reader.GetInt32("doodad_phase_2", -1),
                        reader.GetInt32("doodad_phase_3", -1),
                    ],
                };
                _byDevelopmentId[definition.Id] = definition;
                if (!_byZoneGroup.TryAdd(definition.ZoneGroupId, definition))
                    Logger.Error("Local development {0}: zone group {1} already has a development row; duplicate ignored",
                        definition.Id, definition.ZoneGroupId);
            }
        }

        var boardRowCount = 0;
        var skippedRowCount = 0;
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT id, local_development_id, local_development_board_type_id, show_phase, show_text
                FROM local_development_boards
                """;
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var rowId = reader.GetUInt32("id");
                var developmentId = reader.GetUInt32("local_development_id");
                if (!_byDevelopmentId.TryGetValue(developmentId, out var definition))
                {
                    Logger.Warn("Local development board row {0}: no local_developments row {1}; row skipped", rowId, developmentId);
                    skippedRowCount++;
                    continue;
                }

                var showText = reader.GetString("show_text", string.Empty);
                var threshold = LocalDevelopmentRules.ParseThreshold(showText);
                if (threshold == null)
                {
                    // Loud skip: a board notice without an ASCII digit-run announces no threshold.
                    Logger.Warn("Local development board row {0} (development {1}): show_text carries no ASCII digit-run threshold; row skipped",
                        rowId, developmentId);
                    skippedRowCount++;
                    continue;
                }

                definition.BoardRows.Add(new LocalDevelopmentBoardRow(
                    rowId,
                    reader.GetUInt32("local_development_board_type_id", 0),
                    reader.GetUInt32("show_phase", 0),
                    threshold));
                boardRowCount++;
            }
        }

        Logger.Info("Loaded {0} local developments across {1} zone groups, {2} board threshold rows ({3} skipped)",
            _byDevelopmentId.Count, _byZoneGroup.Count, boardRowCount, skippedRowCount);
    }

    public void PostLoad()
    {
    }

    /// <summary>The development for a zone group, or null when <c>local_developments</c> has no row — the caller skips loudly, never guesses.</summary>
    public LocalDevelopmentDefinition GetByZoneGroup(ushort zoneGroupId) =>
        _byZoneGroup.GetValueOrDefault(zoneGroupId);

    public int DevelopmentCount => _byZoneGroup.Count;

    /// <summary>For tests: seed one development without a database.</summary>
    public void SeedForTest(LocalDevelopmentDefinition definition)
    {
        _byDevelopmentId[definition.Id] = definition;
        _byZoneGroup[definition.ZoneGroupId] = definition;
    }

    /// <summary>For tests: drop every seeded development.</summary>
    public void ResetForTest()
    {
        _byZoneGroup.Clear();
        _byDevelopmentId.Clear();
    }
}
