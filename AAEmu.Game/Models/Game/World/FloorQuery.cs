using System.Numerics;

using AAEmu.Game.Models;

using NLog;

namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Single entry point for "what Z should a unit stand on".
/// Pathfinding (.bai / A*) stays on <see cref="WorldTemplate.GeoData"/>; this type owns Floor only.
/// </summary>
public sealed class FloorQuery
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly WorldTemplate _worldTemplate;
    private readonly Func<Vector3, float> _geoHeight;
    private readonly Func<float, float, float> _terrainHeight;
    private readonly Func<bool> _geoDataEnabled;
    private readonly Func<bool> _heightMapsEnabled;
    private readonly Func<FloorSourceMode> _floorSourceMode;
    private readonly Func<bool> _isMultiFloorWorld;
    private readonly Func<Vector3, float, float?> _navSurfaceHeight;
    private readonly Func<bool> _floorDebug;

    /// <summary>Last hit from <see cref="QueryFloor"/> (debug / GM).</summary>
    public FloorHit LastHit { get; private set; }

    public FloorQuery(WorldTemplate worldTemplate)
        : this(
            worldTemplate,
            pos => worldTemplate.GeoData?.GetHeight(pos) ?? 0f,
            (x, y) => worldTemplate.GetHeight(x, y),
            () => AppConfiguration.Instance.World.GeoDataMode,
            () => AppConfiguration.Instance.HeightMapsEnable,
            () => AppConfiguration.Instance.World.FloorSource,
            () => worldTemplate.ZoneBaiLoader.Count > 0,
            (pos, zHint) => NavSurfaceSampler.TrySample(worldTemplate, pos.X, pos.Y, zHint),
            () => AppConfiguration.Instance.World.FloorDebug)
    {
    }

    /// <summary>Test / custom provider constructor.</summary>
    public FloorQuery(
        WorldTemplate worldTemplate,
        Func<Vector3, float> geoHeight,
        Func<float, float, float> terrainHeight,
        Func<bool> geoDataEnabled,
        Func<bool> heightMapsEnabled,
        Func<FloorSourceMode> floorSourceMode = null,
        Func<bool> isMultiFloorWorld = null,
        Func<Vector3, float, float?> navSurfaceHeight = null,
        Func<bool> floorDebug = null)
    {
        _worldTemplate = worldTemplate;
        _geoHeight = geoHeight ?? (_ => 0f);
        _terrainHeight = terrainHeight ?? ((_, _) => 0f);
        _geoDataEnabled = geoDataEnabled ?? (() => false);
        _heightMapsEnabled = heightMapsEnabled ?? (() => true);
        _floorSourceMode = floorSourceMode ?? (() => FloorSourceMode.Legacy);
        _isMultiFloorWorld = isMultiFloorWorld ?? (() => false);
        _navSurfaceHeight = navSurfaceHeight;
        _floorDebug = floorDebug ?? (() => false);
    }

    /// <summary>
    /// Floor Z at (x,y) using current policy. Matches legacy WorldManager.GetHeight when FloorSource=Legacy.
    /// </summary>
    public float GetFloor(float x, float y, float zHint, FloorContext context)
    {
        return QueryFloor(x, y, zHint, context).Z;
    }

    public FloorHit QueryFloor(float x, float y, float zHint, FloorContext context)
    {
        var pos = new Vector3(x, y, zHint);
        var terrainZ = SampleTerrain(x, y);
        var navNodeZ = SampleNavNode(pos);

        FloorHit hit;
        var mode = _floorSourceMode();

        if (mode == FloorSourceMode.Legacy)
        {
            hit = QueryLegacy(navNodeZ, terrainZ, zHint);
        }
        else if (_isMultiFloorWorld())
        {
            hit = QueryNavSurface(pos, zHint, terrainZ, navNodeZ);
        }
        else
        {
            hit = QueryTerrainFirst(terrainZ, navNodeZ, zHint);
        }

        LastHit = hit;
        if (_floorDebug())
        {
            Logger.Debug(
                "Floor src={0} ctx={1} xyz=({2:0.###},{3:0.###},{4:0.###}) terrain={5:0.###} nav={6:0.###} floor={7:0.###} deltaNav={8:0.###}",
                hit.Source, context, x, y, zHint, hit.TerrainZ, hit.NavNodeZ, hit.Z, hit.DeltaNav);
        }

        return hit;
    }

    /// <summary>
    /// Project (x,y) onto nearby navmesh edges with vertical filter. Used by Path waypoint Z and multi-floor Floor.
    /// </summary>
    public float? TryGetNavSurfaceHeight(float x, float y, float zHint, float maxVerticalSep = 8f, float maxXyRadius = 16f)
    {
        if (_navSurfaceHeight != null)
            return _navSurfaceHeight(new Vector3(x, y, zHint), zHint);

        return NavSurfaceSampler.TrySample(_worldTemplate, x, y, zHint, maxVerticalSep, maxXyRadius);
    }

    /// <summary>
    /// After A*/ReducePath: set each waypoint Z from NavSurface (edge lerp), not raw graph vertex Z.
    /// When a custom nav sampler is injected (tests), it is used per point; otherwise <see cref="NavSurfaceSampler"/>.
    /// </summary>
    public Queue<Vector3> ApplyPathWaypointZ(IEnumerable<Vector3> path)
    {
        if (path == null)
            return new Queue<Vector3>();

        if (_navSurfaceHeight != null)
        {
            var result = new Queue<Vector3>();
            float? prevZ = null;
            foreach (var point in path)
            {
                var zHint = prevZ ?? point.Z;
                var surface = _navSurfaceHeight(new Vector3(point.X, point.Y, zHint), zHint);
                var z = surface ?? point.Z;
                result.Enqueue(new Vector3(point.X, point.Y, z));
                prevZ = z;
            }

            return result;
        }

        return NavSurfaceSampler.ApplyWaypointHeightsQueue(_worldTemplate, path);
    }

    private FloorHit QueryLegacy(float navNodeZ, float terrainZ, float zHint)
    {
        // Mirror WorldManager.GetHeight(zoneKey, x, y, z): prefer GeoData when GeoDataMode, else heightmap.
        if (_geoDataEnabled())
        {
            if (navNodeZ != 0f)
            {
                return new FloorHit
                {
                    Z = navNodeZ,
                    Source = FloorSource.LegacyNavNode,
                    TerrainZ = terrainZ,
                    NavNodeZ = navNodeZ
                };
            }
        }

        if (!_heightMapsEnabled())
        {
            return new FloorHit { Z = 0f, Source = FloorSource.Unchanged, TerrainZ = terrainZ, NavNodeZ = navNodeZ };
        }

        if (terrainZ != 0f)
        {
            return new FloorHit { Z = terrainZ, Source = FloorSource.Terrain, TerrainZ = terrainZ, NavNodeZ = navNodeZ };
        }

        return new FloorHit { Z = zHint, Source = FloorSource.Unchanged, TerrainZ = terrainZ, NavNodeZ = navNodeZ };
    }

    private FloorHit QueryTerrainFirst(float terrainZ, float navNodeZ, float zHint)
    {
        if (_heightMapsEnabled() && terrainZ != 0f)
        {
            return new FloorHit { Z = terrainZ, Source = FloorSource.Terrain, TerrainZ = terrainZ, NavNodeZ = navNodeZ };
        }

        if (_geoDataEnabled() && navNodeZ != 0f)
        {
            return new FloorHit
            {
                Z = navNodeZ,
                Source = FloorSource.LegacyNavNode,
                TerrainZ = terrainZ,
                NavNodeZ = navNodeZ
            };
        }

        return new FloorHit { Z = zHint, Source = FloorSource.Unchanged, TerrainZ = terrainZ, NavNodeZ = navNodeZ };
    }

    private FloorHit QueryNavSurface(Vector3 pos, float zHint, float terrainZ, float navNodeZ)
    {
        var surface = TryGetNavSurfaceHeight(pos.X, pos.Y, zHint);
        if (surface.HasValue)
        {
            return new FloorHit
            {
                Z = surface.Value,
                Source = FloorSource.NavSurface,
                TerrainZ = terrainZ,
                NavNodeZ = navNodeZ
            };
        }

        if (_heightMapsEnabled() && terrainZ != 0f)
        {
            return new FloorHit { Z = terrainZ, Source = FloorSource.Terrain, TerrainZ = terrainZ, NavNodeZ = navNodeZ };
        }

        if (navNodeZ != 0f)
        {
            return new FloorHit
            {
                Z = navNodeZ,
                Source = FloorSource.LegacyNavNode,
                TerrainZ = terrainZ,
                NavNodeZ = navNodeZ
            };
        }

        return new FloorHit { Z = zHint, Source = FloorSource.Unchanged, TerrainZ = terrainZ, NavNodeZ = navNodeZ };
    }

    private float SampleTerrain(float x, float y)
    {
        if (!_heightMapsEnabled())
            return 0f;
        try
        {
            return _terrainHeight(x, y);
        }
        catch
        {
            return 0f;
        }
    }

    private float SampleNavNode(Vector3 pos)
    {
        if (!_geoDataEnabled())
            return 0f;
        try
        {
            return _geoHeight(pos);
        }
        catch
        {
            return 0f;
        }
    }
}
