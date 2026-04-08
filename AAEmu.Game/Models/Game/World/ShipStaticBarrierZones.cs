using System.Numerics;

using AAEmu.Game.Physics;

namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Per-world polylines as infinite-mass ship obstacles (BAI-ingested when <see cref="WorldConfig.GeoDataMode"/> is on).
/// </summary>
public sealed class ShipStaticBarrierZones
{
    public List<ShipStaticBarrier> Barriers { get; } = [];

    /// <summary>Rebuilt when <see cref="Barriers"/>.Count changes; used only by <see cref="ShipStaticBarrierInteraction"/>.</summary>
    internal ShipStaticBarrierSpatialGrid SpatialGrid;

    internal int SpatialGridBuiltForBarrierCount = -1;
}

/// <summary>
/// One polyline barrier: consecutive vertices form wall segments in the world XY plane (game X / game Y → Jitter X / Z).
/// </summary>
public sealed class ShipStaticBarrier
{
    public string Name { get; private init; } = "";
    public uint ZoneKey { get; private init; }
    public float ZMin { get; private init; }
    public float ZMax { get; private init; }
    public float HalfThicknessMeters { get; private init; }
    public bool Enabled { get; private init; } = true;

    /// <summary>Segment endpoints in world X/Y (Jitter X / Jitter Z).</summary>
    internal IReadOnlyList<(float x0, float y0, float x1, float y1)> Segments { get; private init; }

    internal float AabbMinX { get; private init; }
    internal float AabbMaxX { get; private init; }
    internal float AabbMinY { get; private init; }
    internal float AabbMaxY { get; private init; }

    internal static bool TryCreate(ShipStaticBarrierEntryDto entry, out ShipStaticBarrier barrier, out string error)
    {
        barrier = null;
        error = null;

        if (entry is null)
        {
            error = "null entry";
            return false;
        }

        if (entry.PointsXY is null || entry.PointsXY.Count < 2)
        {
            error = $"{entry.Name ?? "?"}: need at least 2 PointsXY";
            return false;
        }

        var pts = new List<Vector2>(entry.PointsXY.Count);
        foreach (var row in entry.PointsXY)
        {
            if (row is null || row.Count < 2)
            {
                error = $"{entry.Name ?? "?"}: invalid PointsXY row";
                return false;
            }

            pts.Add(new Vector2((float)row[0], (float)row[1]));
        }

        if (entry.ZMax < entry.ZMin)
        {
            error = $"{entry.Name ?? "?"}: ZMax < ZMin";
            return false;
        }

        var halfT = entry.HalfThicknessMeters > 0f ? entry.HalfThicknessMeters : 1.05f;
        var segments = new List<(float, float, float, float)>(pts.Count - 1);
        var minX = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var minY = float.PositiveInfinity;
        var maxY = float.NegativeInfinity;

        for (var i = 0; i < pts.Count - 1; i++)
        {
            var a = pts[i];
            var b = pts[i + 1];
            segments.Add((a.X, a.Y, b.X, b.Y));
            minX = MathF.Min(minX, MathF.Min(a.X, b.X));
            maxX = MathF.Max(maxX, MathF.Max(a.X, b.X));
            minY = MathF.Min(minY, MathF.Min(a.Y, b.Y));
            maxY = MathF.Max(maxY, MathF.Max(a.Y, b.Y));
        }

        var pad = halfT + 8f;
        barrier = new ShipStaticBarrier
        {
            Name = entry.Name ?? "",
            ZoneKey = entry.ZoneKey,
            ZMin = entry.ZMin,
            ZMax = entry.ZMax,
            HalfThicknessMeters = halfT,
            Enabled = entry.Enabled,
            Segments = segments,
            AabbMinX = minX - pad,
            AabbMaxX = maxX + pad,
            AabbMinY = minY - pad,
            AabbMaxY = maxY + pad
        };
        return true;
    }
}

internal sealed class ShipStaticBarrierEntryDto
{
    public string Name { get; set; }
    public uint ZoneKey { get; set; }
    public float ZMin { get; set; }
    public float ZMax { get; set; }
    public float HalfThicknessMeters { get; set; } = 1.05f;
    public bool Enabled { get; set; } = true;
    public List<List<double>> PointsXY { get; set; }
}
