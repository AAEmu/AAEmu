using System.Numerics;

using AAEmu.Game.Models.CryEngine.Loaders;
using AAEmuGeoData.Scripts.CryEngine.Mission;

namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Resolves house volumes from loaded <c>areasmission</c> NavigationModifiers.
/// Indoor Floor seating prefers NavSurface inside these polygons; outdoor/cave stay on ByZHint Pick.
/// </summary>
public static class BuildingVolumeQuery
{
    /// <summary>
    /// Reject mission volumes that span caves / huge AI regions (e.g. BuildingId with Height 500).
    /// Real house modifiers on main_world are typically Height 3–70.
    /// </summary>
    public const float MaxBuildingHeightMeters = 80f;

    /// <summary>
    /// Feet sunk through a thin deck (Liliot-style), not outdoor ground under a tall shell.
    /// </summary>
    public const float MaxDeckSinkBelowMinZ = 10f;

    /// <summary>
    /// How far below heightmap counts as underground (small caves near the entrance included).
    /// </summary>
    public const float CaveBelowTerrainEps = 1f;

    /// <summary>
    /// Find the best building volume under (x,y) for seating at <paramref name="zHint"/>.
    /// Strict polygon hit only — no porch XY slack (false outdoor lifts near walls).
    /// </summary>
    public static BuildingVolume? TryFind(WorldTemplate worldTemplate, float x, float y, float zHint)
    {
        if (worldTemplate == null)
            return null;

        var bai = worldTemplate.GetBaiByPos(new Vector3(x, y, zHint));
        return TryFind(bai, x, y, zHint);
    }

    public static BuildingVolume? TryFind(BaseBaiLoader bai, float x, float y, float zHint)
    {
        if (bai?.AreasMissionReaders == null || bai.AreasMissionReaders.Count == 0)
            return null;

        BuildingVolume? best = null;
        var bestScore = float.MaxValue;

        foreach (var reader in bai.AreasMissionReaders)
        {
            foreach (var area in reader.NavigationModifiers)
            {
                if (!TryAsBuildingVolume(area, out var volume))
                    continue;
                if (!IsInPolygonXy(x, y, area.Points))
                    continue;

                var score = Score(volume, zHint);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = volume;
                }
            }
        }

        return best;
    }

    /// <summary>Unit-test / offline helper: classify a parsed modifier without polygon test.</summary>
    public static bool TryAsBuildingVolume(SpecialArea area, out BuildingVolume volume)
    {
        volume = default;
        if (area == null || area.BuildingId == 0)
            return false;

        var height = (float)area.Height;
        if (height <= 0f)
            height = (float)(area.MaxZ - area.MinZ);
        if (height <= 0f || height > MaxBuildingHeightMeters)
            return false;

        volume = new BuildingVolume
        {
            BuildingId = area.BuildingId,
            MinZ = (float)area.MinZ,
            MaxZ = (float)area.MaxZ,
            Height = height
        };
        return true;
    }

    /// <summary>
    /// Idle/hold storey key: keep current feet Z; only lift to home/spawn when sunk a short way
    /// through a thin deck. <see cref="MathF.Max"/>(current, home) would pin multi-storey NPCs to the
    /// upper floor while they stand on a lower one.
    /// </summary>
    public static float StoreyHintForIdle(float currentZ, float anchorZ)
    {
        if (anchorZ <= 0f)
            return currentZ;

        var belowHome = anchorZ - currentZ;
        if (belowHome > 0f && belowHome <= MaxDeckSinkBelowMinZ)
            return anchorZ;

        return currentZ != 0f ? currentZ : anchorZ;
    }

    /// <summary>
    /// Nav/feet clearly under the heightmap → cave/sewer. Must not enter PickInBuilding
    /// (deck filter requires nav above terrain and would bury small caves with BuildingId).
    /// </summary>
    public static bool LooksLikeCave(float terrainZ, float navNodeZ, float zHint,
        float belowEps = CaveBelowTerrainEps)
    {
        if (terrainZ <= 0f)
            return false;

        if (zHint < terrainZ - belowEps)
            return true;

        if (navNodeZ != 0f
            && navNodeZ < terrainZ - belowEps
            && MathF.Abs(navNodeZ - zHint) <= FloorResolver.DefaultVerticalSep)
            return true;

        return false;
    }

    /// <summary>
    /// City/plaza mission shells whose MinZ sits on the heightmap (mall) — not a dug-in house.
    /// Tight band: real house MinZ is usually ≥1 m above foundation dirt (PPZ deck).
    /// </summary>
    public const float FalseShellMinZBand = 0.5f;

    public static bool IsFalseOutdoorShell(BuildingVolume volume, float terrainZ)
    {
        if (terrainZ <= 0f)
            return false;

        var aboveTerrain = volume.MinZ - terrainZ;
        return aboveTerrain >= -FalseShellMinZBand && aboveTerrain <= FalseShellMinZBand;
    }

    public static bool ShouldUseForFloor(BuildingVolume volume, float zHint, float terrainZ)
    {
        if (IsFalseOutdoorShell(volume, terrainZ))
            return false;

        // Volume entirely under the heightmap = cave-like mission box, not a house deck.
        if (terrainZ > 0f && volume.MaxZ < terrainZ - CaveBelowTerrainEps)
            return false;

        if (volume.ContainsZ(zHint, FloorResolver.BuildingStoreySep))
            return true;

        // Thin deck: feet momentarily under MinZ (Liliot) still count as indoor.
        if (zHint < volume.MinZ && volume.MinZ - zHint <= MaxDeckSinkBelowMinZ)
            return true;

        return false;
    }

    /// <summary>Lower is better. Prefer Z inside band; then nearest band edge (sunk under MinZ).</summary>
    public static float Score(BuildingVolume volume, float zHint)
    {
        if (volume.ContainsZ(zHint, slack: 0f))
            return MathF.Abs(zHint - (volume.MinZ + volume.MaxZ) * 0.5f) * 0.001f;

        if (zHint < volume.MinZ)
            return volume.MinZ - zHint;

        return zHint - volume.MaxZ;
    }

    /// <summary>XY point-in-polygon (same ray-cross idea as AiGeodataManager forbidden checks).</summary>
    public static bool IsInPolygonXy(float x, float y, List<Vector3> polygon)
    {
        if (polygon == null || polygon.Count < 3)
            return false;

        var result = false;
        var a = polygon[^1];
        foreach (var b in polygon)
        {
            if (b.X.Equals(x) && b.Y.Equals(y))
                return true;

            if (b.Y < y && a.Y >= y || a.Y < y && b.Y >= y)
            {
                if (b.X + (y - b.Y) / (a.Y - b.Y) * (a.X - b.X) <= x)
                    result = !result;
            }

            a = b;
        }

        return result;
    }
}
