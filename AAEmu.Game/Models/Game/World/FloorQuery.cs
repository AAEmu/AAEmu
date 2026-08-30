using System.Numerics;

using AAEmu.Game.Models;

using NLog;

namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Single entry point for "what Z should a unit stand on" (seating / spawn / skill landing).
/// </summary>
/// <remarks>
/// <para><b>Floor ≠ Path.</b> A* and chase XY use <see cref="WorldTemplate.GeoData"/> (.bai nodes).
/// This type answers a different question: where should the unit's feet be?</para>
/// <para>Before this split, <c>GeoData.GetHeight</c> served both roles. Nearest nav vertices sit above
/// outdoor terrain → floating NPCs (#1425). Path still needs those vertices; seating must not.</para>
/// <para><see cref="FloorSourceMode"/> (config / GM <c>floorsource</c>):</para>
/// <list type="bullet">
/// <item><description><see cref="FloorSourceMode.TerrainFirst"/> — heightmap Blerp on open world.</description></item>
/// <item><description><see cref="FloorSourceMode.Legacy"/> — old rule: nav node then terrain (A/B rollback).</description></item>
/// </list>
/// <para>Worlds with zone <c>.bai</c> (<see cref="WorldTemplate.ZoneBaiLoader"/>) use
/// <see cref="FloorProvider.NavSurface"/> + zHint for multi-floor / caves (#1033 partial).</para>
/// </remarks>
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

    /// <summary>Last hit from <see cref="QueryFloor"/> — same data as return value; for GM /height.</summary>
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

    /// <summary>Test / custom provider constructor. Default mode matches production (<see cref="FloorSourceMode.TerrainFirst"/>).</summary>
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
        _floorSourceMode = floorSourceMode ?? (() => FloorSourceMode.TerrainFirst);
        _isMultiFloorWorld = isMultiFloorWorld ?? (() => false);
        _navSurfaceHeight = navSurfaceHeight;
        _floorDebug = floorDebug ?? (() => false);
    }

    /// <summary>Floor Z at (x,y). Matches legacy WorldManager.GetHeight when mode is Legacy.</summary>
    public float GetFloor(float x, float y, float zHint, FloorContext context = FloorContext.Move)
    {
        return QueryFloor(x, y, zHint, context).Z;
    }

    public FloorHit QueryFloor(float x, float y, float zHint, FloorContext context = FloorContext.Move)
    {
        var pos = new Vector3(x, y, zHint);
        var terrainZ = SampleTerrain(x, y);
        var navNodeZ = SampleNavNode(pos);

        var mode = _floorSourceMode();
        FloorHit hit = mode switch
        {
            FloorSourceMode.Legacy => QueryLegacy(navNodeZ, terrainZ, zHint),
            _ when _isMultiFloorWorld() => QueryNavSurface(pos, zHint, terrainZ, navNodeZ),
            _ => QueryTerrainFirst(terrainZ, navNodeZ, zHint)
        };

        LastHit = hit;
        if (_floorDebug())
        {
            Logger.Debug(
                "Floor mode={0} src={1} ctx={2} xyz=({3:0.###},{4:0.###},{5:0.###}) terrain={6:0.###} nav={7:0.###} floor={8:0.###} deltaNav={9:0.###}",
                mode, hit.Provider, context, x, y, zHint, hit.TerrainZ, hit.NavNodeZ, hit.Z, hit.DeltaNav);
        }

        return hit;
    }

    /// <summary>
    /// Project (x,y) onto nearby navmesh edges with vertical filter.
    /// Used by path waypoint Z and multi-floor Floor — not for outdoor TerrainFirst seating.
    /// </summary>
    public float? TryGetNavSurfaceHeight(float x, float y, float zHint, float maxVerticalSep = 8f, float maxXyRadius = 16f)
    {
        if (_navSurfaceHeight != null)
            return _navSurfaceHeight(new Vector3(x, y, zHint), zHint);

        return NavSurfaceSampler.TrySample(_worldTemplate, x, y, zHint, maxVerticalSep, maxXyRadius);
    }

    /// <summary>
    /// After A*/ReducePath: rewrite waypoint Z from nav-surface edge lerp, not raw graph vertex Z.
    /// Path XY still comes from GeoData; this only smooths chase height. MoveTowards re-seats via GetFloor each tick.
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
        // Mirror pre-split WorldManager.GetHeight: GeoData node when enabled, else heightmap.
        if (_geoDataEnabled() && navNodeZ != 0f)
        {
            return new FloorHit
            {
                Z = navNodeZ,
                Provider = FloorProvider.LegacyNavNode,
                TerrainZ = terrainZ,
                NavNodeZ = navNodeZ
            };
        }

        if (!_heightMapsEnabled())
            return new FloorHit { Z = 0f, Provider = FloorProvider.Unchanged, TerrainZ = terrainZ, NavNodeZ = navNodeZ };

        if (terrainZ != 0f)
            return new FloorHit { Z = terrainZ, Provider = FloorProvider.Terrain, TerrainZ = terrainZ, NavNodeZ = navNodeZ };

        return new FloorHit { Z = zHint, Provider = FloorProvider.Unchanged, TerrainZ = terrainZ, NavNodeZ = navNodeZ };
    }

    private FloorHit QueryTerrainFirst(float terrainZ, float navNodeZ, float zHint)
    {
        if (_heightMapsEnabled() && terrainZ != 0f)
            return new FloorHit { Z = terrainZ, Provider = FloorProvider.Terrain, TerrainZ = terrainZ, NavNodeZ = navNodeZ };

        if (_geoDataEnabled() && navNodeZ != 0f)
        {
            return new FloorHit
            {
                Z = navNodeZ,
                Provider = FloorProvider.LegacyNavNode,
                TerrainZ = terrainZ,
                NavNodeZ = navNodeZ
            };
        }

        return new FloorHit { Z = zHint, Provider = FloorProvider.Unchanged, TerrainZ = terrainZ, NavNodeZ = navNodeZ };
    }

    private FloorHit QueryNavSurface(Vector3 pos, float zHint, float terrainZ, float navNodeZ)
    {
        var surface = TryGetNavSurfaceHeight(pos.X, pos.Y, zHint);
        if (surface.HasValue)
        {
            return new FloorHit
            {
                Z = surface.Value,
                Provider = FloorProvider.NavSurface,
                TerrainZ = terrainZ,
                NavNodeZ = navNodeZ
            };
        }

        if (_heightMapsEnabled() && terrainZ != 0f)
            return new FloorHit { Z = terrainZ, Provider = FloorProvider.Terrain, TerrainZ = terrainZ, NavNodeZ = navNodeZ };

        if (navNodeZ != 0f)
        {
            return new FloorHit
            {
                Z = navNodeZ,
                Provider = FloorProvider.LegacyNavNode,
                TerrainZ = terrainZ,
                NavNodeZ = navNodeZ
            };
        }

        return new FloorHit { Z = zHint, Provider = FloorProvider.Unchanged, TerrainZ = terrainZ, NavNodeZ = navNodeZ };
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
