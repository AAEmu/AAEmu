using System.Collections.Concurrent;

using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.GameData;

/// <summary>
/// <c>const_doodad_types</c> by name: the shipped named system doodads (house_for_sale, mailbox,
/// siege marks, ...) live in content rather than as template ids in code.
/// </summary>
[GameData]
public class ConstDoodadTypeGameData : Singleton<ConstDoodadTypeGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    // Concurrent: parallel test classes seed these rows while other tests read them.
    private readonly ConcurrentDictionary<string, uint> _values = new(StringComparer.Ordinal);

    public void Load(SqliteConnection connection)
    {
        _values.Clear();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT name, doodad_almighty_id
            FROM const_doodad_types
            """;
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var name = reader.GetString("name", string.Empty);
            if (string.IsNullOrEmpty(name))
                continue;
            _values[name] = (uint)reader.GetInt64("doodad_almighty_id", 0);
        }

        Logger.Info("Loaded {0} const doodad types", _values.Count);
    }

    public void PostLoad()
    {
    }

    public bool TryGet(string name, out uint value) => _values.TryGetValue(name, out value);

    /// <summary>Required row. Missing content must fail loudly, not fall back to a literal.</summary>
    public uint Require(string name)
    {
        if (TryGet(name, out var value))
            return value;
        throw new InvalidOperationException($"Required const_doodad_types row '{name}' is missing.");
    }

    /// <summary>For tests: seeds values without a database.</summary>
    public void SetForTest(string name, uint value) => _values[name] = value;
}
