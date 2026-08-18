using System.Numerics;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models;
using NLog;

namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Unit placement floor policy. Sample the XY heightmap, then snap with lift/drop caps.
/// Do not use <c>GeoData.GetHeight</c> for standing units — that picks the nearest .bai node in
/// 3D space, so a high probe (stage/rift) snaps to cliff lips and units bounce.
/// Requires <c>HeightMapsEnable</c> (set true in Config.Local.json for this stack).
/// </summary>
public static class TerrainFloor
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>Dedicate spawn points sit slightly above the sampled floor.</summary>
    public const float ClearanceMetres = 0.4f;

    /// <summary>Within this band of ground, sit on the floor (+ clearance).</summary>
    public const float NearBandMetres = 3f;

    /// <summary>Max lift onto a sampled floor; larger gaps are cliff / bad samples.</summary>
    public const float MaxUpwardSnapMetres = 8f;

    /// <summary>Longest drop from a portal/rift ball onto terrain; deeper keeps raw Z.</summary>
    public const float MaxDownwardSnapMetres = 12f;

    /// <summary>Water must clear the seabed by this much before we prefer the surface.</summary>
    public const float WaterAboveSeabedMetres = 1f;

    /// <summary>
    /// Heightmap-only sample at continent XY. Returns 0 when heightmaps are off or missing.
    /// </summary>
    public static float SampleHeightmap(WorldInstance world, float x, float y)
    {
        if (world == null || !AppConfiguration.Instance.HeightMapsEnable)
            return 0f;
        try
        {
            return world.GetHeight(x, y);
        }
        catch
        {
            return 0f;
        }
    }

    /// <summary>
    /// Heightmap-only sample via the world template that owns <paramref name="zoneId"/>.
    /// </summary>
    public static float SampleHeightmap(uint zoneId, float x, float y)
    {
        if (zoneId == 0 || !AppConfiguration.Instance.HeightMapsEnable)
            return 0f;
        try
        {
            var template = WorldManager.Instance.GetWorldTemplateByZoneKey(zoneId);
            return template?.GetHeight(x, y) ?? 0f;
        }
        catch
        {
            return 0f;
        }
    }

    /// <summary>
    /// True when the probe is over water and a surface Z is available (not the seabed).
    /// Open ocean: heightmap is the seabed below <c>OceanLevel</c>. Event plots (Lusca) often
    /// place markers above the plane (~120) so <c>IsWater(rawZ)</c> is false — do not clamp Z
    /// into <c>IsWater</c> (ocean is a global plane and would mark dry land as water).
    /// </summary>
    public static bool TryWaterSurface(WorldInstance world, Vector3 probe, out float waterSurfaceZ)
    {
        waterSurfaceZ = 0f;
        if (world == null)
            return false;

        var ocean = world.Template?.OceanLevel ?? world.Water?.OceanLevel ?? 0f;

        if (world.IsWater(probe))
        {
            waterSurfaceZ = world.Water?.GetWaterSurface(probe, out _) ?? ocean;
            return waterSurfaceZ > 0f;
        }

        // Plot above the ocean plane: treat as open sea only when heightmap is clearly seabed.
        if (ocean <= 0f)
            return false;
        var ground = SampleHeightmap(world, probe.X, probe.Y);
        if (ground <= 0f || ground >= ocean - WaterAboveSeabedMetres)
            return false;

        waterSurfaceZ = ocean;
        return true;
    }

    /// <summary>
    /// Pure snap: near-band sit, mild underground lift, short drop, water surface, else keep raw.
    /// Never invent a floor from stage/caster altitude — pass heightmap (or 0) as <paramref name="ground"/>.
    /// </summary>
    /// <param name="logTag">Optional id for Info logs (e.g. SpawnEffect SubType).</param>
    public static float ChooseUnitFloorZ(
        float rawZ,
        float ground,
        bool overWater,
        float waterSurfaceZ,
        uint logTag = 0)
    {
        if (ground <= 0f)
            return rawZ;

        if (overWater && waterSurfaceZ > ground + WaterAboveSeabedMetres)
        {
            Logger.Info(
                "TerrainFloor water snap tag={0} rawZ={1:F1} ground={2:F1} → water={3:F1}",
                logTag, rawZ, ground, waterSurfaceZ);
            return OnFloor(waterSurfaceZ);
        }

        if (rawZ >= ground - NearBandMetres && rawZ <= ground + NearBandMetres)
            return OnFloor(ground);

        if (rawZ < ground - NearBandMetres)
        {
            var lift = ground - rawZ;
            if (lift > MaxUpwardSnapMetres)
            {
                Logger.Info(
                    "TerrainFloor keep raw tag={0} rawZ={1:F1} ignored ground={2:F1} (lift {3:F1} m > {4:F0} m)",
                    logTag, rawZ, ground, lift, MaxUpwardSnapMetres);
                return OnFloor(rawZ);
            }

            Logger.Info(
                "TerrainFloor lift tag={0} rawZ={1:F1} → ground={2:F1}",
                logTag, rawZ, ground);
            return OnFloor(ground);
        }

        var drop = rawZ - ground;
        if (drop <= MaxDownwardSnapMetres)
        {
            Logger.Info(
                "TerrainFloor drop tag={0} rawZ={1:F1} → ground={2:F1}",
                logTag, rawZ, ground);
            return OnFloor(ground);
        }

        Logger.Info(
            "TerrainFloor keep raw tag={0} rawZ={1:F1} ignored ground={2:F1} (drop {3:F1} m > {4:F0} m)",
            logTag, rawZ, ground, drop, MaxDownwardSnapMetres);
        return rawZ;
    }

    private static float OnFloor(float ground) => ground + ClearanceMetres;
}
