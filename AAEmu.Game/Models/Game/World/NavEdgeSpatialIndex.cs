using System.Numerics;

using AAEmu.Game.Models.CryEngine.Entities;
using AAEmu.Game.Models.CryEngine.Loaders;

namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// XY grid of navmesh edges for bounded <see cref="NavSurfaceSampler"/> queries (Greptile P2).
/// </summary>
public sealed class NavEdgeSpatialIndex
{
    public const float DefaultCellSize = NavSurfaceSampler.DefaultMaxXyRadius;

    private readonly float _cellSize;
    private readonly Dictionary<(int cx, int cy), List<(NodeDescriptor A, NodeDescriptor B)>> _cells = new();

    public int EdgeInsertions { get; private set; }
    public int CellCount => _cells.Count;

    public NavEdgeSpatialIndex(float cellSize = DefaultCellSize)
    {
        _cellSize = cellSize > 0f ? cellSize : DefaultCellSize;
    }

    public static NavEdgeSpatialIndex Build(BaseBaiLoader bai, float cellSize = DefaultCellSize)
    {
        var index = new NavEdgeSpatialIndex(cellSize);
        if (bai == null)
            return index;

        foreach (var net in bai.NetMissionReaders)
        {
            foreach (var link in net.LinkDescriptorList)
            {
                var a = link.SourceNodeDescriptor;
                var b = link.TargetNodeDescriptor;
                if (a == null || b == null)
                    continue;
                index.AddEdge(a, b);
            }
        }

        return index;
    }

    public void AddEdge(NodeDescriptor a, NodeDescriptor b)
    {
        var minX = MathF.Min(a.Pos.X, b.Pos.X);
        var maxX = MathF.Max(a.Pos.X, b.Pos.X);
        var minY = MathF.Min(a.Pos.Y, b.Pos.Y);
        var maxY = MathF.Max(a.Pos.Y, b.Pos.Y);

        var c0x = FloorDiv(minX);
        var c1x = FloorDiv(maxX);
        var c0y = FloorDiv(minY);
        var c1y = FloorDiv(maxY);

        for (var cx = c0x; cx <= c1x; cx++)
        {
            for (var cy = c0y; cy <= c1y; cy++)
            {
                var key = (cx, cy);
                if (!_cells.TryGetValue(key, out var list))
                {
                    list = [];
                    _cells[key] = list;
                }

                list.Add((a, b));
                EdgeInsertions++;
            }
        }
    }

    /// <summary>
    /// Visit unique edges whose XY AABB may intersect the query disk.
    /// </summary>
    public void ForEachNear(float x, float y, float radius, Action<NodeDescriptor, NodeDescriptor> visitor)
    {
        if (visitor == null || _cells.Count == 0)
            return;

        var r = MathF.Max(0f, radius);
        var minCx = FloorDiv(x - r);
        var maxCx = FloorDiv(x + r);
        var minCy = FloorDiv(y - r);
        var maxCy = FloorDiv(y + r);

        // Deduplicate edges that span multiple cells (same node pair references).
        var seen = new HashSet<(NodeDescriptor A, NodeDescriptor B)>();

        for (var cx = minCx; cx <= maxCx; cx++)
        {
            for (var cy = minCy; cy <= maxCy; cy++)
            {
                if (!_cells.TryGetValue((cx, cy), out var list))
                    continue;

                foreach (var (a, b) in list)
                {
                    if (!seen.Add((a, b)))
                        continue;
                    visitor(a, b);
                }
            }
        }
    }

    private int FloorDiv(float v)
    {
        return (int)MathF.Floor(v / _cellSize);
    }
}
