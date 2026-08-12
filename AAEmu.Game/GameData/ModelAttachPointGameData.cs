using System.Numerics;
using System.Xml.Linq;

using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.IO;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

using Newtonsoft.Json;

using NLog;

namespace AAEmu.Game.GameData;

/// <summary>
/// Attach point offsets for every model that housing and slaves bind doodads to, resolved from the shipped
/// static data rather than from a hand-kept table.
///
/// The chain is entirely within that data: a model names a prefab, a prefab names the meshes it places and
/// where it places them, and a mesh carries named helper nodes. An attach point is the helper whose name
/// the attach point table gives it, expressed as an offset from the model.
///
/// Invariants:
///   - every element of a model's selected state contributes, since an attach point may be defined by any
///     of them and reading a subset leaves points unresolved
///   - a model that defines no helper for an attach point leaves it unresolved, which is distinct from
///     resolving it to the origin
///   - resolution is deterministic: elements are read in a fixed order, and a helper defined twice at
///     different positions is reported rather than settled by enumeration order
///
/// Resolution is expensive, so the result is cached beside the other static data and rebuilt only when its
/// inputs change: the configured static-data sources, or this resolver's format version, so that a change
/// to what the resolver reads discards a cache that would otherwise still look current.
/// </summary>
[GameData]
public class ModelAttachPointGameData : Singleton<ModelAttachPointGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private const string CacheFileName = "model_attach_points.cache.json";

    /// <summary>
    /// Part of the cache identity. Bump it whenever resolution changes what it reads or how it merges,
    /// so that caches produced under the previous rules are discarded rather than reused.
    /// </summary>
    private const int CacheFormatVersion = 2;

    private Dictionary<uint, Dictionary<AttachPointKind, WorldSpawnPosition>> _attachPoints = [];

    /// <summary>Attach point id → the '$' helper name that carries it in a mesh.</summary>
    private Dictionary<AttachPointKind, string> _helperNames = [];

    /// <summary>
    /// The model state a resolved attach point belongs to: the one the state table names 'normal'.
    /// </summary>
    /// <remarks>
    /// Read from the shipped state table rather than assumed, so the value follows the data. Models that
    /// do not define it fall back to their lowest state, which is why this is a preference rather than a
    /// filter - selecting on it alone would resolve nothing for those models.
    /// </remarks>
    private uint _normalModelStateId = DefaultNormalModelStateId;

    /// <summary>Used only when the state table is missing or does not name a normal state.</summary>
    private const uint DefaultNormalModelStateId = 1;

    private const string NormalModelStateName = "normal";

    /// <summary>Parsed prefab libraries, held only while the cache is being built.</summary>
    private readonly Dictionary<string, Dictionary<string, List<(string Mesh, Vector3 Offset)>>> _prefabMeshCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Helper nodes per mesh; scenery meshes repeat across prefabs, so they are only read once.</summary>
    private readonly Dictionary<string, Dictionary<string, Vector3>> _meshHelperCache =
        new(StringComparer.OrdinalIgnoreCase);

    public bool HasData => _attachPoints.Count > 0;

    public void Load(SqliteConnection connection)
    {
        _attachPoints = [];
        _normalModelStateId = LoadNormalModelStateId(connection);
        _helperNames = LoadHelperNames(connection);
        if (_helperNames.Count == 0)
        {
            Logger.Warn("model_attach_point_strings is empty; attach points will fall back to the json tables");
            return;
        }

        var stamp = BuildCacheStamp();
        if (TryLoadCache(stamp, out var cached))
        {
            _attachPoints = cached;
            Logger.Info($"Loaded {_attachPoints.Count} model attach point sets from cache");
            return;
        }

        var modelIds = LoadModelsOfInterest(connection);
        if (modelIds.Count == 0)
            return;

        Logger.Info($"Resolving attach points for {modelIds.Count} models from the client data (first run, this is cached afterwards)...");

        var meshless = 0;
        foreach (var modelId in modelIds)
        {
            var brushes = ResolveMeshes(connection, modelId);
            if (brushes.Count == 0)
            {
                meshless++;
                continue;
            }

            // Attach helpers may be spread across any of a model's meshes, so all of them contribute, each
            // shifted by where its brush is placed. Order is fixed upstream so the merge is repeatable.
            var candidates = new List<(string Name, Vector3 Position)>();
            foreach (var (meshPath, brushOffset) in brushes)
            {
                foreach (var (name, local) in ReadMeshHelpers(meshPath))
                    candidates.Add((name, local + brushOffset));
            }

            var helpers = MergeHelpers(candidates, out var conflicts);
            foreach (var conflict in conflicts)
                Logger.Warn($"Model {modelId} defines attach helper {conflict} at more than one position; using the first");

            if (helpers.Count == 0)
                continue;

            var points = new Dictionary<AttachPointKind, WorldSpawnPosition>();
            foreach (var (attachPoint, helperName) in _helperNames)
            {
                if (!helpers.TryGetValue(helperName, out var p))
                    continue;

                points[attachPoint] = new WorldSpawnPosition { X = p.X, Y = p.Y, Z = p.Z };
            }

            if (points.Count > 0)
                _attachPoints[modelId] = points;
        }

        _prefabMeshCache.Clear();
        _meshHelperCache.Clear();
        Logger.Info($"Resolved attach points for {_attachPoints.Count} models ({meshless} had no reachable mesh)");
        SaveCache(stamp);
    }

    public void PostLoad()
    {
        // Nothing to resolve here; consumers read the table in their own PostLoad.
    }

    /// <summary>Attach points for a model, or null when the model has none.</summary>
    public Dictionary<AttachPointKind, WorldSpawnPosition> GetAttachPoints(uint modelId)
    {
        return _attachPoints.GetValueOrDefault(modelId);
    }

    /// <summary>Single attach point for a model, or null.</summary>
    public WorldSpawnPosition GetAttachPoint(uint modelId, AttachPointKind attachPoint)
    {
        var set = _attachPoints.GetValueOrDefault(modelId);
        return set != null && set.TryGetValue(attachPoint, out var pos) ? pos : null;
    }

    /// <summary>
    /// The id of the state named <see cref="NormalModelStateName"/>, or
    /// <see cref="DefaultNormalModelStateId"/> when the table cannot supply one.
    /// </summary>
    private static uint LoadNormalModelStateId(SqliteConnection connection)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id FROM enum_model_states WHERE name=@name";
            command.Parameters.AddWithValue("@name", NormalModelStateName);
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            if (reader.Read())
                return reader.GetUInt32("id", DefaultNormalModelStateId);
        }
        catch (Exception exception)
        {
            Logger.Warn(exception, "Could not read the model state table; assuming {0} is the normal state",
                DefaultNormalModelStateId);
        }

        return DefaultNormalModelStateId;
    }

    /// <summary>
    /// Combines helper definitions from every mesh of a model into one set, reporting any name defined at
    /// more than one position.
    /// </summary>
    /// <remarks>
    /// The first definition of a name wins, so the caller must supply <paramref name="candidates"/> in a
    /// deterministic order or the resolved position becomes a function of enumeration order. A name that
    /// appears again at a different position is a genuine ambiguity in the data rather than something to
    /// resolve silently, so it is reported through <paramref name="conflicts"/>.
    /// </remarks>
    internal static Dictionary<string, Vector3> MergeHelpers(
        IReadOnlyList<(string Name, Vector3 Position)> candidates, out List<string> conflicts)
    {
        const float samePositionTolerance = 0.0001f;

        var merged = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        conflicts = [];

        foreach (var (name, position) in candidates)
        {
            if (merged.TryGetValue(name, out var existing))
            {
                if (Vector3.Distance(existing, position) > samePositionTolerance && reported.Add(name))
                    conflicts.Add(name);
                continue;
            }

            merged[name] = position;
        }

        return merged;
    }

    private static Dictionary<AttachPointKind, string> LoadHelperNames(SqliteConnection connection)
    {
        var res = new Dictionary<AttachPointKind, string>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, prefab FROM model_attach_point_strings";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var prefab = reader.GetString("prefab", string.Empty);
            if (string.IsNullOrWhiteSpace(prefab) || !prefab.StartsWith('$'))
                continue;
            res[(AttachPointKind)reader.GetInt16("id")] = prefab;
        }
        return res;
    }

    /// <summary>Every model housing or a slave can bind something to — the only ones worth resolving.</summary>
    private static List<uint> LoadModelsOfInterest(SqliteConnection connection)
    {
        var ids = new HashSet<uint>();

        void Collect(string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var id = reader.GetUInt32("model_id", 0);
                if (id > 0)
                    ids.Add(id);
            }
        }

        Collect("SELECT DISTINCT main_model_id AS model_id FROM housings");
        Collect("SELECT DISTINCT model_id FROM housing_build_steps");
        Collect("SELECT DISTINCT model_id FROM slaves");

        return [.. ids];
    }

    /// <summary>
    /// models → the cgf the mesh actually lives in. <paramref name="brushOffset"/> is where the prefab places
    /// that mesh, which the helper positions are relative to.
    /// </summary>
    private List<(string Mesh, Vector3 Offset)> ResolveMeshes(SqliteConnection connection, uint modelId)
    {
        string subType = null;
        var subId = 0u;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT sub_id, sub_type FROM models WHERE id=@id";
            command.Parameters.AddWithValue("@id", modelId);
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            if (reader.Read())
            {
                subId = reader.GetUInt32("sub_id", 0);
                subType = reader.GetString("sub_type", string.Empty);
            }
        }

        if (subId == 0 || string.IsNullOrEmpty(subType))
            return [];

        // Invariant: every element of the model's selected state takes part in resolution. A model is
        // described by several elements at once, and an attach point may be defined by any of them, so
        // reading a subset silently leaves points unresolved.
        //
        // The state is the one named 'normal' where the model defines it, falling back to the lowest state
        // present for models that do not. Ordering is explicit so that a repeated helper resolves the same
        // way on every run.
        var uris = subType switch
        {
            "PrefabModel" => QueryList(connection,
                """
                SELECT file_path AS uri FROM prefab_elements
                 WHERE prefab_model_id=@id
                   AND state_id = (SELECT state_id FROM prefab_elements
                                    WHERE prefab_model_id=@id
                                    ORDER BY (state_id<>@normalState), state_id
                                    LIMIT 1)
                 ORDER BY id
                """, subId, _normalModelStateId),
            "ShipModel" => QueryList(connection, "SELECT normal AS uri FROM ship_models WHERE id=@id", subId),
            "VehicleModel" => QueryList(connection, "SELECT normal AS uri FROM vehicle_models WHERE id=@id", subId),
            // ActorModel is a character rig; its attach points are bones in a .chr, not helpers in a .cgf.
            _ => []
        };

        return [.. uris.SelectMany(ResolvePrefabUri)];
    }

    private static List<string> QueryList(SqliteConnection connection, string sql, uint id,
        uint? normalState = null)
    {
        var res = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@id", id);
        if (normalState.HasValue)
            command.Parameters.AddWithValue("@normalState", normalState.Value);
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var uri = reader.GetString("uri", string.Empty);
            if (!string.IsNullOrEmpty(uri))
                res.Add(uri);
        }
        return res;
    }

    /// <summary>
    /// "prefab://prefabs/housing_farm.xml/housing_farm.step1" → the cgf its Brush references.
    /// A cgf path may also be given directly as "cgf://objects/…".
    /// </summary>
    private List<(string Mesh, Vector3 Offset)> ResolvePrefabUri(string uri)
    {
        var scheme = uri.IndexOf("://", StringComparison.Ordinal);
        if (scheme < 0)
            return [];

        var kind = uri[..scheme];
        var rest = uri[(scheme + 3)..].Replace('\\', '/');

        if (kind.StartsWith("cgf", StringComparison.OrdinalIgnoreCase) ||
            kind.StartsWith("cga", StringComparison.OrdinalIgnoreCase))
            return [(ToClientPath(rest), Vector3.Zero)];

        if (!kind.Equals("prefab", StringComparison.OrdinalIgnoreCase))
            return [];

        var xmlEnd = rest.IndexOf(".xml/", StringComparison.OrdinalIgnoreCase);
        if (xmlEnd < 0)
            return [];

        var libraryPath = ToClientPath(rest[..(xmlEnd + 4)]);
        var prefabName = rest[(xmlEnd + 5)..];

        var meshes = GetPrefabMeshes(libraryPath);
        return meshes.TryGetValue(prefabName, out var brushes) ? brushes : [];
    }

    private static string ToClientPath(string relative)
    {
        relative = relative.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
        return relative.StartsWith("game/", StringComparison.Ordinal) ? relative : "game/" + relative;
    }

    /// <summary>Prefab name → every brush it places, parsed once per library.</summary>
    private Dictionary<string, List<(string Mesh, Vector3 Offset)>> GetPrefabMeshes(string libraryPath)
    {
        if (_prefabMeshCache.TryGetValue(libraryPath, out var cached))
            return cached;

        var result = new Dictionary<string, List<(string, Vector3)>>(StringComparer.OrdinalIgnoreCase);
        _prefabMeshCache[libraryPath] = result;

        var xml = ClientFileManager.GetFileAsString(libraryPath);
        if (string.IsNullOrWhiteSpace(xml))
        {
            Logger.Trace($"prefab library not found: {libraryPath}");
            return result;
        }

        try
        {
            var doc = XDocument.Parse(xml);
            foreach (var prefab in doc.Descendants("Prefab"))
            {
                var name = (string)prefab.Attribute("Name");
                if (string.IsNullOrEmpty(name) || result.ContainsKey(name))
                    continue;

                var brushes = new List<(string, Vector3)>();
                foreach (var brush in prefab.Descendants("Object"))
                {
                    if ((string)brush.Attribute("Type") != "Brush")
                        continue;
                    var mesh = (string)brush.Attribute("Prefab");
                    if (string.IsNullOrEmpty(mesh))
                        continue;

                    brushes.Add((ToClientPath(mesh), ParseVector((string)brush.Attribute("Pos"))));
                }

                result[name] = brushes;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not parse prefab library {libraryPath}: {ex.Message}");
        }

        return result;
    }

    private static Vector3 ParseVector(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Vector3.Zero;

        var parts = value.Split(',');
        if (parts.Length < 3)
            return Vector3.Zero;

        return float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) &&
               float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y) &&
               float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z)
            ? new Vector3(x, y, z)
            : Vector3.Zero;
    }

    private Dictionary<string, Vector3> ReadMeshHelpers(string meshPath)
    {
        if (_meshHelperCache.TryGetValue(meshPath, out var cached))
            return cached;

        var helpers = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        _meshHelperCache[meshPath] = helpers;

        using var stream = ClientFileManager.GetFileStream(meshPath);
        if (stream == null)
            return helpers;

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        foreach (var (name, pos) in CgfHelperReader.ReadHelpers(ms.ToArray(), meshPath))
            helpers[name] = pos;

        return helpers;
    }

    #region Cache

    private sealed class CacheFile
    {
        public string Stamp { get; set; }
        public Dictionary<uint, Dictionary<AttachPointKind, WorldSpawnPosition>> Models { get; set; }
    }

    private static string CachePath => Path.Combine(FileManager.AppPath, "Data", CacheFileName);

    /// <summary>Identity of the client data behind the cache, so a new pak rebuilds it.</summary>
    /// <summary>
    /// Identity of the currently loadable cache: this resolver's format version together with a
    /// descriptor for each configured static-data source.
    /// </summary>
    private static string BuildCacheStamp() =>
        ComposeCacheStamp(CacheFormatVersion, ClientFileManager.Sources.Select(DescribeSource));

    /// <summary>
    /// Builds the cache identity from its two inputs.
    /// </summary>
    /// <remarks>
    /// The format version participates so that changing what the resolver reads discards a cache whose
    /// sources are otherwise unchanged. Without it such a cache still matches, and the resolver keeps
    /// serving results produced by the previous rules.
    /// </remarks>
    internal static string ComposeCacheStamp(int formatVersion, IEnumerable<string> sourceDescriptors) =>
        string.Join(";", new[] { $"v{formatVersion}" }.Concat(sourceDescriptors));

    /// <summary>
    /// Whether a cache carrying <paramref name="cachedStamp"/> may be used now. Any difference rebuilds:
    /// the stamp is an identity, not a version to compare for order.
    /// </summary>
    internal static bool IsCacheCurrent(string cachedStamp, string currentStamp) =>
        !string.IsNullOrEmpty(cachedStamp) && string.Equals(cachedStamp, currentStamp, StringComparison.Ordinal);

    /// <summary>
    /// Describes one configured source well enough that replacing it changes the cache identity.
    /// </summary>
    private static string DescribeSource(ClientSource source)
    {
        try
        {
            var info = new FileInfo(source.PathName);
            return info.Exists
                ? $"{source.PathName}|{info.Length}|{info.LastWriteTimeUtc:O}"
                : $"{source.PathName}|dir";
        }
        catch
        {
            return source.PathName;
        }
    }

    private static bool TryLoadCache(string stamp, out Dictionary<uint, Dictionary<AttachPointKind, WorldSpawnPosition>> data)
    {
        data = null;
        try
        {
            if (!File.Exists(CachePath))
                return false;

            var cache = JsonConvert.DeserializeObject<CacheFile>(File.ReadAllText(CachePath));
            if (cache?.Models == null || cache.Models.Count == 0)
                return false;
            if (!IsCacheCurrent(cache.Stamp, stamp))
            {
                Logger.Info("Attach point cache no longer matches its inputs; rebuilding");
                return false;
            }

            data = cache.Models;
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not read the attach point cache, rebuilding: {ex.Message}");
            return false;
        }
    }

    private void SaveCache(string stamp)
    {
        try
        {
            var path = CachePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonConvert.SerializeObject(
                new CacheFile { Stamp = stamp, Models = _attachPoints }, Formatting.Indented));
            Logger.Info($"Wrote attach point cache to {path}");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not write the attach point cache: {ex.Message}");
        }
    }

    #endregion
}
