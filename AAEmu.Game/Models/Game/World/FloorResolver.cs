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
    /// Same-storey window inside a building volume (metres). Tall house modifiers are ~40 m tall
    /// and span 1F/2F; accepting any nav in <c>[MinZ,MaxZ]</c> caused idle flicker between floors.
    /// </summary>
    public const float BuildingStoreySep = 3.5f;

    /// <summary>
    /// Walkable house decks sit on nav above the heightmap foundation (PPZ 1F: terrain ~137, deck ~139).
    /// Foundation nav at terrain level must not win just because it is nearest to spawner Z.
    /// </summary>
    public const float BuildingDeckAboveTerrain = 1.5f;

    /// <summary>True when nav looks like a house deck (above foundation dirt), not cave/plaza dirt.</summary>
    public static bool IsPlausibleBuildingDeck(float navZ, float terrainZ)
    {
        if (terrainZ == 0f)
            return true;

        return navZ >= terrainZ + BuildingDeckAboveTerrain;
    }

    /// <summary>
    /// Inside an AI building volume: seat on the nav deck of the caller's storey, not heightmap
    /// under the foundation and not a different floor just because the volume is tall.
    /// Candidates must lie in the volume band; terrain is never used.
    /// </summary>
    public static FloorHit PickInBuilding(
        float zHint,
        float terrainZ,
        float? navSurfaceZ,
        float navNodeZ,
        BuildingVolume volume,
        float storeySep = BuildingStoreySep)
    {
        if (TryPickBuildingCandidate(zHint, terrainZ, navSurfaceZ, navNodeZ, volume, storeySep,
                acrossStoreys: false, out var hit))
            return hit;

        // Never heightmap inside a house — terrain under the foundation is below the deck.
        return new FloorHit
        {
            Z = zHint,
            Provider = FloorProvider.Unchanged,
            TerrainZ = terrainZ,
            NavNodeZ = navNodeZ
        };
    }

    /// <summary>
    /// Spawn seating inside a building: pick nav closest to spawner Z across storeys (DB hint),
    /// still constrained to the volume band.
    /// </summary>
    public static FloorHit PickInBuildingAtSpawn(
        float zHint,
        float terrainZ,
        float? navSurfaceZ,
        float navNodeZ,
        BuildingVolume volume)
    {
        if (TryPickBuildingCandidate(zHint, terrainZ, navSurfaceZ, navNodeZ, volume, BuildingStoreySep,
                acrossStoreys: true, out var hit))
            return hit;

        return new FloorHit
        {
            Z = zHint,
            Provider = FloorProvider.Unchanged,
            TerrainZ = terrainZ,
            NavNodeZ = navNodeZ
        };
    }

    /// <summary>
    /// Shared indoor picker. <paramref name="acrossStoreys"/> ignores the same-storey window and
    /// keeps the candidate nearest to <paramref name="zHint"/> inside the volume (spawn).
    /// </summary>
    private static bool TryPickBuildingCandidate(
        float zHint,
        float terrainZ,
        float? navSurfaceZ,
        float navNodeZ,
        BuildingVolume volume,
        float storeySep,
        bool acrossStoreys,
        out FloorHit hit)
    {
        float? bestZ = null;
        var bestDist = float.MaxValue;
        var bestProvider = FloorProvider.Unchanged;

        void Consider(float? z, FloorProvider provider)
        {
            if (!z.HasValue || z.Value == 0f)
                return;
            if (!IsPlausibleBuildingDeck(z.Value, terrainZ))
                return;
            // Stay inside the mission volume (rejects plaza/wrong-cell nav leaking in).
            if (!volume.ContainsZ(z.Value, storeySep))
                return;
            if (!acrossStoreys && MathF.Abs(z.Value - zHint) > storeySep)
                return;

            var dist = MathF.Abs(z.Value - zHint);
            if (dist >= bestDist)
                return;

            bestDist = dist;
            bestZ = z.Value;
            bestProvider = provider;
        }

        Consider(navSurfaceZ, FloorProvider.NavSurface);
        Consider(navNodeZ != 0f ? navNodeZ : null, FloorProvider.LegacyNavNode);

        if (!bestZ.HasValue)
        {
            hit = default;
            return false;
        }

        hit = new FloorHit
        {
            Z = bestZ.Value,
            Provider = bestProvider,
            TerrainZ = terrainZ,
            NavNodeZ = navNodeZ
        };
        return true;
    }

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
