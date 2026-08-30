namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Picks a floor Z from candidate providers using the unit's vertical hint (<c>zHint</c>).
/// </summary>
/// <remarks>
/// <para>
/// Industry parallel — this is the same idea as multilayer geodata / nav projection:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <b>L2J / Lineage 2</b>: <c>getHeight(x,y,z)</c> walks geodata layers at XY and keeps the layer
/// nearest to the caller's Z (multilevel cells).
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>TrinityCore</b>: <c>VMapManager::getHeight(..., z, maxSearchDist)</c> searches for a floor
/// in a bounded vertical window from Z (wrong window → wrong floor / floor above).
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Recast/Detour / Unreal</b>: <c>findNearestPoly</c> / <c>ProjectPointToNavigation</c> use
/// query extents (especially Z) so multi-storey meshes resolve to the intended level;
/// <c>ProjectPointMulti</c> returns every hit in a Z range for explicit filtering.
/// </description>
/// </item>
/// </list>
/// <para>
/// AAEmu has no L2-style packed layers; candidates are heightmap Blerp and/or nav-edge projection
/// (<see cref="NavSurfaceSampler"/>). Pathfinding stays on GeoData/A* — Floor ≠ Path (#1425 / #1033).
/// </para>
/// </remarks>
public static class FloorResolver
{
    /// <summary>
    /// Vertical window around <c>zHint</c> when preferring a candidate (metres).
    /// Aligned with <see cref="NavSurfaceSampler.DefaultMaxVerticalSep"/> so cave floors near the
    /// unit win over surface heightmap hundreds of metres above (Ronbann / sewers, #1033).
    /// </summary>
    public const float DefaultVerticalSep = 12f;

    /// <summary>
    /// Choose the floor hit among optional terrain / nav-surface samples.
    /// </summary>
    /// <param name="zHint">Caller Z (spawn, current feet, previous waypoint) — the layer key.</param>
    /// <param name="terrainZ">Heightmap Blerp, or 0 if unavailable.</param>
    /// <param name="navSurfaceZ">Edge-projected nav height near zHint, or null.</param>
    /// <param name="navNodeZ">Nearest raw .bai vertex (diagnostic / last-resort only).</param>
    /// <param name="verticalSep">Max |candidate − zHint| to treat as same storey.</param>
    public static FloorHit Pick(
        float zHint,
        float terrainZ,
        float? navSurfaceZ,
        float navNodeZ,
        float verticalSep = DefaultVerticalSep)
    {
        // Pass 1: same-storey candidates (L2 "nearest layer", TC maxSearchDist window).
        FloorProvider bestProvider = FloorProvider.Unchanged;
        var bestZ = 0f;
        var bestDist = float.MaxValue;
        var foundInWindow = false;

        Consider(FloorProvider.Terrain, terrainZ != 0f ? terrainZ : null);
        Consider(FloorProvider.NavSurface, navSurfaceZ);

        if (foundInWindow)
        {
            return new FloorHit
            {
                Z = bestZ,
                Provider = bestProvider,
                TerrainZ = terrainZ,
                NavNodeZ = navNodeZ
            };
        }

        // Pass 2: nothing near zHint — outdoor heal / open ground.
        // Prefer heightmap so units that drifted onto high nav vertices snap back (#1425).
        // Do NOT prefer raw nearest nav node here: that reintroduces outdoor float and wrong cave layers.
        if (terrainZ != 0f)
        {
            return new FloorHit
            {
                Z = terrainZ,
                Provider = FloorProvider.Terrain,
                TerrainZ = terrainZ,
                NavNodeZ = navNodeZ
            };
        }

        if (navSurfaceZ.HasValue)
        {
            return new FloorHit
            {
                Z = navSurfaceZ.Value,
                Provider = FloorProvider.NavSurface,
                TerrainZ = terrainZ,
                NavNodeZ = navNodeZ
            };
        }

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

        return new FloorHit
        {
            Z = zHint,
            Provider = FloorProvider.Unchanged,
            TerrainZ = terrainZ,
            NavNodeZ = navNodeZ
        };

        void Consider(FloorProvider provider, float? z)
        {
            if (!z.HasValue || z.Value == 0f)
                return;

            var dist = MathF.Abs(z.Value - zHint);
            if (dist > verticalSep)
                return;

            // Strict < : equal distance keeps the first candidate. Terrain is considered before
            // NavSurface so outdoor ties prefer the heightmap.
            if (dist < bestDist)
            {
                bestDist = dist;
                bestZ = z.Value;
                bestProvider = provider;
                foundInWindow = true;
            }
        }
    }
}
