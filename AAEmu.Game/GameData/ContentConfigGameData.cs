using System.Collections.Concurrent;

using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.GameData;

/// <summary>
/// <c>content_configs</c> by <c>enum_content_configs.name</c>. Content rules such as the Hero's Mobilization
/// Order limits or territory tax bounds live here rather than in code.
/// </summary>
[GameData]
public class ContentConfigGameData : Singleton<ContentConfigGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    // Concurrent: parallel test classes seed these rows while other tests read them.
    // The other test-seeded game data stores use the same type for the same reason.
    private readonly ConcurrentDictionary<string, long> _values = new(StringComparer.Ordinal);

    public void Load(SqliteConnection connection)
    {
        _values.Clear();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT e.name, c.value
            FROM content_configs c
            JOIN enum_content_configs e ON e.id = c.id
            """;
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var name = reader.GetString("name", string.Empty);
            if (string.IsNullOrEmpty(name) || reader.IsDBNull("value"))
                continue;
            _values[name] = reader.GetInt64("value");
        }

        Logger.Info("Loaded {0} content configs", _values.Count);
    }

    public void PostLoad()
    {
    }

    public bool TryGet(string name, out long value) => _values.TryGetValue(name, out value);

    public bool TryGetInt(string name, out int value)
    {
        if (_values.TryGetValue(name, out var raw))
        {
            value = (int)raw;
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>Required row. Missing content must fail loudly, not fall back to a literal.</summary>
    public int RequireInt(string name)
    {
        if (TryGetInt(name, out var value))
            return value;
        throw new InvalidOperationException($"Required content_configs row '{name}' is missing.");
    }

    public uint RequireUInt(string name) => (uint)RequireInt(name);

    /// <summary>The configured value, or <paramref name="fallback"/> when the row is absent.</summary>
    public long Get(string name, long fallback) => _values.TryGetValue(name, out var value) ? value : fallback;

    public int GetInt(string name, int fallback) => (int)Get(name, fallback);

    public uint GetUInt(string name, uint fallback) => (uint)Get(name, fallback);

    /// <summary>For tests: seeds values without a database.</summary>
    public void SetForTest(string name, long value) => _values[name] = value;

    /// <summary>For tests: drops a seeded row so the missing-row path can be exercised.</summary>
    public void RemoveForTest(string name) => _values.TryRemove(name, out _);
}
