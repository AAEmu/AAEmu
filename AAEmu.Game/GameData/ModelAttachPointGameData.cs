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
/// Attach point offsets for every model housing and slaves bind doodads to, resolved from the client's own
/// data instead of a hand-kept table.
///
/// The chain is entirely in the shipped data:
///   models.sub_id + sub_type  →  prefab_elements.file_path (PrefabModel) or
///                                ship_models.normal / vehicle_models.normal (Ship/VehicleModel)
///                             →  prefab://prefabs/&lt;lib&gt;.xml/&lt;prefab&gt;  →  game/prefabs/&lt;lib&gt;.xml
///                             →  &lt;Object Type="Brush" Prefab="…cgf"&gt;
///                             →  that cgf's '$' helper nodes, named by model_attach_point_strings.prefab
///
/// Reading ~900 meshes out of a 68 GB game_pak is not something to repeat every boot, so the resolved table
/// is cached next to the other client data and rebuilt only when the pak changes.
/// </summary>
[GameData]
public class ModelAttachPointGameData : Singleton<ModelAttachPointGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private const string CacheFileName = "model_attach_points.cache.json";

    private Dictionary<uint, Dictionary<AttachPointKind, WorldSpawnPosition>> _attachPoints = [];

    /// <summary>Attach point id → the '$' helper name that carries it in a mesh.</summary>
    private Dictionary<AttachPointKind, string> _helperNames = [];

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
        _helperNames = LoadHelperNames(connection);
        if (_helperNames.Count == 0)
        {
            Logger.Warn("model_attach_point_strings is empty; attach points will fall back to the json tables");
            return;
        }

        var stamp = BuildClientDataStamp();
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

            // A house prefab is dozens of brushes — the building shells plus scenery. The attach helpers sit
            // in the building meshes and can be spread across several of them, so every brush is read and the
            // results merged, each shifted by where the prefab places that brush.
            var helpers = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
            foreach (var (meshPath, brushOffset) in brushes)
            {
                foreach (var (name, local) in ReadMeshHelpers(meshPath))
                    helpers.TryAdd(name, local + brushOffset);
            }

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

        var uri = subType switch
        {
            "PrefabModel" => QueryScalar(connection,
                "SELECT file_path AS uri FROM prefab_elements WHERE prefab_model_id=@id ORDER BY (state_id<>1), state_id LIMIT 1", subId),
            "ShipModel" => QueryScalar(connection, "SELECT normal AS uri FROM ship_models WHERE id=@id", subId),
            "VehicleModel" => QueryScalar(connection, "SELECT normal AS uri FROM vehicle_models WHERE id=@id", subId),
            // ActorModel is a character rig; its attach points are bones in a .chr, not helpers in a .cgf.
            _ => null
        };

        return string.IsNullOrEmpty(uri) ? [] : ResolvePrefabUri(uri);
    }

    private static string QueryScalar(SqliteConnection connection, string sql, uint id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@id", id);
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        return reader.Read() ? reader.GetString("uri", string.Empty) : null;
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
    private static string BuildClientDataStamp()
    {
        var parts = new List<string>();
        foreach (var source in ClientFileManager.Sources)
        {
            try
            {
                var info = new FileInfo(source.PathName);
                parts.Add(info.Exists
                    ? $"{source.PathName}|{info.Length}|{info.LastWriteTimeUtc:O}"
                    : $"{source.PathName}|dir");
            }
            catch
            {
                parts.Add(source.PathName);
            }
        }
        return string.Join(";", parts);
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
            if (!string.Equals(cache.Stamp, stamp, StringComparison.Ordinal))
            {
                Logger.Info("Client data changed since the attach point cache was written; rebuilding");
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
