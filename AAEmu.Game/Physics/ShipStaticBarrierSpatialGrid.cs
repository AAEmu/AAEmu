#nullable enable

using System.Collections.Generic;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Physics;

/// <summary>
/// Uniform grid over XZ for ship-barrier broadphase: each segment is inserted into every cell
/// overlapping its axis-aligned bounds (expanded). Query uses the ship center ± shipPad box.
/// </summary>
internal sealed class ShipStaticBarrierSpatialGrid
{
    /// <summary>World XZ cell size (m).</summary>
    public const float CellSizeMeters = 128f;

    /// <summary>
    /// Half-width expansion of each segment AABB before insertion (must be ≥ max expected
    /// <c>halfLen + halfBeam + ShipBoundsPadMeters</c> from <see cref="ShipStaticBarrierInteraction.BarrierDefaults"/>).
    /// </summary>
    public const float SegmentInsertExpandMeters = 200f;

    private readonly Dictionary<(int Gx, int Gy), List<SegmentRef>> _cells = new();

    private readonly struct SegmentRef
    {
        public ShipStaticBarrier Barrier { get; init; }
        public int SegmentIndex { get; init; }
    }

    /// <summary>Rebuilds the grid from the full barrier list (call when <c>Barriers.Count</c> changes).</summary>
    public void Rebuild(IReadOnlyList<ShipStaticBarrier> barriers)
    {
        _cells.Clear();
        for (var bi = 0; bi < barriers.Count; bi++)
        {
            var barrier = barriers[bi];
            if (!barrier.Enabled)
                continue;

            var halfT = barrier.HalfThicknessMeters;
            var expand = SegmentInsertExpandMeters;
            var segList = barrier.Segments;
            for (var si = 0; si < segList.Count; si++)
            {
                var seg = segList[si];
                var minX = MathF.Min(seg.x0, seg.x1) - halfT - expand;
                var maxX = MathF.Max(seg.x0, seg.x1) + halfT + expand;
                var minZ = MathF.Min(seg.y0, seg.y1) - halfT - expand;
                var maxZ = MathF.Max(seg.y0, seg.y1) + halfT + expand;
                InsertIntoCells(new SegmentRef { Barrier = barrier, SegmentIndex = si }, minX, maxX, minZ, maxZ);
            }
        }
    }

    private void InsertIntoCells(SegmentRef segRef, float minX, float maxX, float minZ, float maxZ)
    {
        var gx0 = (int)Math.Floor(minX / CellSizeMeters);
        var gx1 = (int)Math.Floor(maxX / CellSizeMeters);
        var gy0 = (int)Math.Floor(minZ / CellSizeMeters);
        var gy1 = (int)Math.Floor(maxZ / CellSizeMeters);
        for (var gx = gx0; gx <= gx1; gx++)
        {
            for (var gy = gy0; gy <= gy1; gy++)
            {
                var key = (gx, gy);
                if (!_cells.TryGetValue(key, out var list))
                {
                    list = [];
                    _cells[key] = list;
                }

                list.Add(segRef);
            }
        }
    }

    /// <summary>
    /// Fills <paramref name="outList"/> with unique (barrier, segment index) pairs whose grid cells
    /// intersect the ship broadphase box [scx ± shipPad] × [scz ± shipPad].
    /// </summary>
    public void Query(
        float scx,
        float scz,
        float shipPad,
        List<(ShipStaticBarrier barrier, int segmentIndex)> outList,
        HashSet<(ShipStaticBarrier barrier, int segmentIndex)> dedupe)
    {
        outList.Clear();
        dedupe.Clear();
        if (_cells.Count == 0)
            return;

        var minX = scx - shipPad;
        var maxX = scx + shipPad;
        var minZ = scz - shipPad;
        var maxZ = scz + shipPad;
        var gx0 = (int)Math.Floor(minX / CellSizeMeters);
        var gx1 = (int)Math.Floor(maxX / CellSizeMeters);
        var gy0 = (int)Math.Floor(minZ / CellSizeMeters);
        var gy1 = (int)Math.Floor(maxZ / CellSizeMeters);

        for (var gx = gx0; gx <= gx1; gx++)
        {
            for (var gy = gy0; gy <= gy1; gy++)
            {
                if (!_cells.TryGetValue((gx, gy), out var list))
                    continue;
                for (var i = 0; i < list.Count; i++)
                {
                    var r = list[i];
                    var key = (r.Barrier, r.SegmentIndex);
                    if (dedupe.Add(key))
                        outList.Add((r.Barrier, r.SegmentIndex));
                }
            }
        }
    }
}
