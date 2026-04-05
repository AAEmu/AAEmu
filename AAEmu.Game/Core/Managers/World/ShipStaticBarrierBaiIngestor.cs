using System.Numerics;
using System.Runtime.CompilerServices;
using AAEmu.Game.Models;
using AAEmu.Game.Models.CryEngine.Loaders;
using AAEmu.Game.Models.CryEngine.Mission;
using AAEmu.Game.Models.CryEngine.Readers;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Lazily converts coastal/near-sea <see cref="AreasMissionReader"/> polygons into <see cref="ShipStaticBarrier"/> entries.
/// Constants live in this file (no extra config). Runs only when <see cref="WorldConfig.GeoDataMode"/> is on.
/// </summary>
internal static class ShipStaticBarrierBaiIngestor
{
    [Flags]
    private enum BaiLayers
    {
        Forbidden = 1,
        Boundaries = 2,
        Designer = 4
    }

    /// <summary>Matches test-branch defaults (absolute Z was 95–115 at ocean ~100 → offsets from template ocean).</summary>
    private static class Defaults
    {
        public const float ZMinOffsetFromOcean = -5f;
        public const float ZMaxOffsetFromOcean = 15f;
        public const float HalfThicknessMeters = 1.8f;
        public const float CoastalMaxAboveOcean = 25f;
        public const float CoastalMaxBelowOcean = 40f;
        public const int MaritimeSampleGrid = 4;
        public const int MaxTotalBarriers = 100_000;
        public const BaiLayers Layers = BaiLayers.Forbidden | BaiLayers.Boundaries | BaiLayers.Designer;
    }

    public static void EnsureCell(WorldInstance world, int cellX, int cellY)
    {
        if (!AppConfiguration.Instance.World.GeoDataMode)
            return;
        if (world.ShipStaticBarriers is null)
            return;

        if (cellX < 0 || cellY < 0 || cellX >= world.Template.CellX || cellY >= world.Template.CellY)
            return;

        lock (world.ShipStaticBarriersMutationLock)
        {
            if (!world.ShipBarrierBaiIngestedCells.Add((cellX, cellY)))
                return;

            if (world.ShipStaticBarriers.Barriers.Count >= Defaults.MaxTotalBarriers)
                return;

            var cell = world.Template.GetCell(cellX, cellY);
            if (cell is null)
                return;

            cell.VerifyCellLoaded();

            var layers = Defaults.Layers;
            var seenLoaders = new HashSet<BaseBaiLoader>(ReferenceEqualityComparer.Instance);

            for (var py = 0; py < 4; py++)
            {
                for (var px = 0; px < 4; px++)
                {
                    var loader = cell.BaiLoader[px, py];
                    if (loader is null || !seenLoaders.Add(loader))
                        continue;

                    foreach (var reader in loader.AreasMissionReaders)
                    {
                        if (world.ShipStaticBarriers.Barriers.Count >= Defaults.MaxTotalBarriers)
                            return;

                        TryAddList(world, reader.ForbiddenBoundariesList, layers, BaiLayers.Boundaries, "fb");
                        TryAddList(world, reader.DesignerForbiddenAreasList, layers, BaiLayers.Designer, "df");
                        TryAddList(world, reader.ForbiddenAreasList, layers, BaiLayers.Forbidden, "fa");
                    }
                }
            }
        }
    }

    private static void TryAddList(
        WorldInstance world,
        List<AiShape> shapes,
        BaiLayers enabled,
        BaiLayers layer,
        string shortTag)
    {
        if ((enabled & layer) == 0 || shapes.Count == 0)
            return;

        var ocean = world.Template.OceanLevel;
        var zMin = ocean + Defaults.ZMinOffsetFromOcean;
        var zMax = ocean + Defaults.ZMaxOffsetFromOcean;

        foreach (var shape in shapes)
        {
            if (world.ShipStaticBarriers!.Barriers.Count >= Defaults.MaxTotalBarriers)
                return;

            if (shape.Points is null || shape.Points.Count < 2)
                continue;

            if (!IsMaritimePolygon(world, shape))
                continue;

            var pointsXy = ClosedPolylinePointsXy(shape.Points);
            if (pointsXy.Count < 2)
                continue;

            var name = $"bai_{shortTag}_{world.ShipBarrierBaiNameSerial++}_{SanitizeName(shape.Name)}";
            var dto = new ShipStaticBarrierEntryDto
            {
                Name = name,
                ZoneKey = 0u,
                ZMin = zMin,
                ZMax = zMax,
                HalfThicknessMeters = Defaults.HalfThicknessMeters,
                Enabled = true,
                PointsXY = pointsXy
            };

            if (ShipStaticBarrier.TryCreate(dto, out var barrier, out _))
                world.ShipStaticBarriers.Barriers.Add(barrier);
        }
    }

    /// <summary>Same heuristic as test branch (inland reject, pier Z band, heightmap samples).</summary>
    public static bool IsMaritimePolygon(WorldInstance world, AiShape shape)
    {
        var ocean = world.Template.OceanLevel;
        var maxBelow = MathF.Max(0f, Defaults.CoastalMaxBelowOcean);
        var maxAbove = MathF.Max(0f, Defaults.CoastalMaxAboveOcean);
        var extendedBelow = MathF.Max(maxBelow, 180f);
        var grid = Math.Clamp(Defaults.MaritimeSampleGrid, 1, 12);

        var minX = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var minY = float.PositiveInfinity;
        var maxY = float.NegativeInfinity;
        var vMinZ = float.PositiveInfinity;
        var vMaxZ = float.NegativeInfinity;
        foreach (var p in shape.Points)
        {
            minX = MathF.Min(minX, p.X);
            maxX = MathF.Max(maxX, p.X);
            minY = MathF.Min(minY, p.Y);
            maxY = MathF.Max(maxY, p.Y);
            vMinZ = MathF.Min(vMinZ, p.Z);
            vMaxZ = MathF.Max(vMaxZ, p.Z);
        }

        if (float.IsInfinity(minX))
            return false;

        if (vMinZ > ocean + 55f)
            return false;

        if (vMaxZ >= ocean - 45f && vMinZ <= ocean + 55f)
            return true;

        for (var ix = 0; ix < grid; ix++)
        {
            var tx = grid == 1 ? 0.5f : ix / (float)(grid - 1);
            for (var iy = 0; iy < grid; iy++)
            {
                var ty = grid == 1 ? 0.5f : iy / (float)(grid - 1);
                var sx = minX + (maxX - minX) * tx;
                var sy = minY + (maxY - minY) * ty;
                var h = world.GetHeight(sx, sy);
                if (h >= ocean - extendedBelow && h <= ocean + maxAbove)
                    return true;
            }
        }

        return false;
    }

    private static List<List<double>> ClosedPolylinePointsXy(List<Vector3> pts)
    {
        var r = new List<List<double>>(pts.Count + 1);
        foreach (var p in pts)
            r.Add([(double)p.X, (double)p.Y]);

        if (pts.Count >= 2)
        {
            var f = pts[0];
            var l = pts[^1];
            if (Math.Abs(f.X - l.X) > 0.01f || Math.Abs(f.Y - l.Y) > 0.01f)
                r.Add([(double)f.X, (double)f.Y]);
        }

        return r;
    }

    private static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "unnamed";
        var s = name.Trim();
        return s.Length > 64 ? s[..64] : s;
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<BaseBaiLoader>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public bool Equals(BaseBaiLoader x, BaseBaiLoader y) => ReferenceEquals(x, y);
        public int GetHashCode(BaseBaiLoader obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
