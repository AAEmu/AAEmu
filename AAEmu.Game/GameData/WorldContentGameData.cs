using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.GameData;

/// <summary>
/// The world-content table: which named contents of each category this server has configured. The client
/// is told about them in the world-content pack it parses on entering the world, and its per-category
/// features consult what came back.
/// </summary>
/// <remarks>
/// The table's <c>content_type</c> is the client's category name (<c>craft</c>, <c>zone</c>, ...) and its
/// <c>name</c> is the configured entry inside that category. Types the client has no category for are
/// reported and skipped, never guessed onto a neighbouring category.
/// </remarks>
[GameData]
public class WorldContentGameData : Singleton<WorldContentGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly List<WorldContentGroup> _groups = [];
    private readonly Dictionary<byte, WorldContentGroup> _groupsById = [];

    public void Load(SqliteConnection connection)
    {
        _groups.Clear();
        _groupsById.Clear();

        var namesPerGroup = new Dictionary<byte, HashSet<string>>();
        var unknownTypes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var rows = 0;

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, content_type, content_id, name FROM world_contents";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);

            while (reader.Read())
            {
                rows++;
                var type = reader.GetString("content_type");
                var name = reader.GetString("name");

                var categoryId = WorldContentFilterPack.CategoryIdOf(type);
                if (categoryId == null)
                {
                    unknownTypes[type] = unknownTypes.GetValueOrDefault(type) + 1;
                    continue;
                }

                if (!_groupsById.TryGetValue(categoryId.Value, out var group))
                {
                    group = new WorldContentGroup
                    {
                        CategoryId = categoryId.Value,
                        CategoryName = type.Trim().ToLowerInvariant()
                    };
                    _groupsById[categoryId.Value] = group;
                    _groups.Add(group);
                    namesPerGroup[categoryId.Value] = new HashSet<string>(StringComparer.Ordinal);
                }

                if (namesPerGroup[categoryId.Value].Add(name))
                    group.Names.Add(name);
            }
        }

        foreach (var group in _groups)
            Logger.Info("World content: category {0}({1}) has {2} entries", group.CategoryName, group.CategoryId, group.Names.Count);

        foreach (var (type, count) in unknownTypes)
            Logger.Warn("World content: {0} rows of content_type '{1}' have no client category and were skipped", count, type);

        if (rows == 0)
            Logger.Warn("World content: world_contents is empty, so the pack will configure nothing");
    }

    public void PostLoad()
    {
    }

    /// <summary>The pack as the client expects it, built from the configured contents.</summary>
    public byte[] BuildPack()
    {
        return WorldContentFilterPack.Serialize(_groups);
    }

    public IReadOnlyList<WorldContentGroup> Groups => _groups;
}
