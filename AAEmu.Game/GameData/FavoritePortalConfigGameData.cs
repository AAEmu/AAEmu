using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.GameData;

[GameData]
public sealed class FavoritePortalConfigGameData : Singleton<FavoritePortalConfigGameData>, IGameDataLoader
{
    public const string DefaultFavoritePortalLimit = "default_favorite_portal_limit";

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly object _sync = new();
    private Dictionary<uint, long> _valuesByConfigId = [];

    public void Load(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var loaded = new Dictionary<uint, long>();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT e.id AS enum_id, c.id AS config_id, c.kind_id, c.value " +
            "FROM enum_content_configs e " +
            "LEFT JOIN content_configs c ON c.id = e.id " +
            "WHERE e.name = @name";
        command.Parameters.AddWithValue("@name", DefaultFavoritePortalLimit);
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var id = reader.GetUInt32("enum_id");
            if (reader.IsDBNull("config_id") || reader.IsDBNull("kind_id") || reader.IsDBNull("value"))
            {
                throw new InvalidOperationException(
                    $"Favorite portal config '{DefaultFavoritePortalLimit}' has a null or malformed row for id {id}.");
            }

            var kind = reader.GetInt64("kind_id");
            if (kind <= 0)
            {
                throw new InvalidOperationException(
                    $"Favorite portal config '{DefaultFavoritePortalLimit}' has invalid kind {kind} for id {id}.");
            }

            if (!loaded.TryAdd(id, reader.GetInt64("value")))
            {
                throw new InvalidOperationException(
                    $"Favorite portal config '{DefaultFavoritePortalLimit}' has duplicate config id {id}.");
            }
        }

        lock (_sync)
            _valuesByConfigId = loaded;
        Logger.Info("Loaded {0} favorite portal config rows", loaded.Count);
    }

    public void PostLoad()
    {
        var value = ResolveDefaultLimit();
        if (value < 0)
            throw new InvalidOperationException($"Favorite portal default limit is negative: {value}.");
    }

    public int RequireDefaultLimit()
    {
        var value = ResolveDefaultLimit();
        if (value < 0)
            throw new InvalidOperationException($"Favorite portal default limit is negative: {value}.");
        return value;
    }

    private int ResolveDefaultLimit()
    {
        lock (_sync)
        {
            if (_valuesByConfigId.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Favorite portal config '{DefaultFavoritePortalLimit}' requires exactly one row; found {_valuesByConfigId.Count}.");
            }

            return checked((int)_valuesByConfigId.Values.Single());
        }
    }

    public void RemoveForTest()
    {
        lock (_sync)
            _valuesByConfigId = [];
    }
}
