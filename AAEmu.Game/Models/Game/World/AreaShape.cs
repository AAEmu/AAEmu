using System.Numerics;

using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.World;
public class AreaShape
{
    public uint Id { get; set; }
    public AreaShapeType Type { get; set; }
    public float Value1 { get; set; }
    public float Value2 { get; set; }
    public float Value3 { get; set; }

    /// <summary>
    /// aoe_shapes.area_target_kind_id (enum_plot_area_target_kinds). For <see cref="AreaShapeType.Line"/>
    /// this names the unit the corridor is aimed at; the corridor itself starts at the plot event's Source.
    /// </summary>
    public AreaTargetKindType AreaTargetKind { get; set; } = AreaTargetKindType.None;

    /// <summary>
    /// Whether <c>aoe_shapes.value3</c> on a sphere is the FULL sweep of the cone (so the accepted
    /// bearing is ±value3/2) rather than the half-angle (±value3).
    /// </summary>
    /// <remarks>
    /// Settled from the shipped data: it is the full sweep. Of the 18,582 sphere rows, 323 carry a
    /// non-zero value3 — 108 of them exactly 360, and the rest 0.3 to 200. A half-angle reading has to
    /// explain 180 and 200, and cannot: 180 would be an omni cone spelled differently from the 360 rows
    /// beside it, and 200 would be ±200°, a 400° arc, which is not a bearing range at all. As a full sweep
    /// the same rows read naturally — 45 is a 45° cone (34 rows), 90 a quarter circle (20), 120 (10), 180
    /// a half circle (6, e.g. shape 19121 on 맹독 45493) and 200 a 200° arc (shapes 18214 and 21872, both
    /// on 칼릴의 검 44784/47828), while 360 stays the omni marker. 195 of the cone shapes are reached by a
    /// live plot event, so the half-angle they filter at is value3/2.
    /// </remarks>
    private const bool SphereConeValue3IsFullSweep = true;

    /// <summary>
    /// Sphere cone half-angle in degrees: the bearing from the origin's facing that still counts as a hit.
    /// On <see cref="AreaShapeType.Sphere"/> content stores the radius in <see cref="Value1"/> and the cone
    /// in <see cref="Value3"/>. Cuboid uses Value3 as vertical extent instead.
    /// </summary>
    public float SphereConeHalfAngleDegrees => Type == AreaShapeType.Sphere
        ? (SphereConeValue3IsFullSweep ? Value3 / 2f : Value3)
        : 0f;

    public List<T> ComputeCuboid<T>(GameObject origin, List<T> toCheck) where T : GameObject
    {
        // Z check
        var zOffset = Value3;
        toCheck = toCheck.Where(o => o.Transform.World.Position.Z >= origin.Transform.World.Position.Z - zOffset && o.Transform.World.Position.Z <= origin.Transform.World.Position.Z + zOffset).ToList();
        if (toCheck.Count == 0)
            return toCheck;

        // Triangle check
        var vertices = MathUtil.GetCuboidVertices(Value1, Value2,
            origin.Transform.World.Position.X, origin.Transform.World.Position.Y,
            //origin.Transform.World.ToRollPitchYawSBytes().Item3);
            origin.Transform.World.Rotation.Z);

        toCheck = toCheck.Where(o =>
        {
            var tri1 = MathUtil.PointInTriangle((o.Transform.World.Position.X, o.Transform.World.Position.Y), vertices[0], vertices[1],
                vertices[2]);

            // Fan from vertex 0, not (v1,v2,v3): the quad's two triangles have to share a diagonal
            // through the SAME corner. Splitting it as (v0,v1,v2)+(v1,v2,v3) leaves a quarter-wedge
            // of every cuboid uncovered, so an entire quarter of each box AoE never registered a hit.
            var tri2 = MathUtil.PointInTriangle((o.Transform.World.Position.X, o.Transform.World.Position.Y), vertices[0], vertices[2],
                vertices[3]);

            return tri1 || tri2;
        }).ToList();

        return toCheck;
    }

    /// <summary>
    /// Line AoE (enum_aoe_shape_kinds 3): a corridor starting at <paramref name="start"/>, running
    /// <see cref="Value2"/> metres along <paramref name="forward"/>, <see cref="Value1"/> metres to
    /// each side and ±<see cref="Value3"/> metres vertically.
    /// </summary>
    /// <remarks>
    /// Deliberately not built on <see cref="ComputeCuboid"/>. A box centred on the aimed-at unit puts
    /// that unit exactly on the rectangle's diagonal, and <see cref="MathUtil.PointInTriangle"/> tests
    /// strictly (Sign(...) &lt; 0), so a point on an edge is rejected — the search then returned nothing
    /// and every per-target edge behind it expanded to zero hits.
    /// </remarks>
    public List<T> ComputeCorridor<T>(Vector3 start, Vector3 forward, List<T> toCheck, float aimDistance = 0f) where T : GameObject
    {
        if (toCheck == null || toCheck.Count == 0)
            return toCheck ?? [];

        var dir = new Vector3(forward.X, forward.Y, 0f);
        if (dir.LengthSquared() < 0.0001f)
            return [];
        dir = Vector3.Normalize(dir);

        // value2 == 0 means "as far as the anchor", otherwise it is a fixed reach.
        var length = Value2 > 0f ? Value2 : aimDistance;
        if (length <= 0f)
            return [];

        var halfWidth = Value1;
        var zOffset = Value3;

        return toCheck.Where(o =>
        {
            var p = o.Transform.World.Position;
            if (p.Z < start.Z - zOffset || p.Z > start.Z + zOffset)
                return false;

            var dx = p.X - start.X;
            var dy = p.Y - start.Y;
            var along = dx * dir.X + dy * dir.Y;
            if (along < -o.ModelSize || along > length + o.ModelSize)
                return false;

            var side = dx * -dir.Y + dy * dir.X;
            return Math.Abs(side) <= halfWidth + o.ModelSize;
        }).ToList();
    }

    /// <summary>
    /// Keeps units whose bearing from <paramref name="origin"/> facing is within ±Value3 degrees.
    /// </summary>
    public List<T> FilterSphereCone<T>(GameObject origin, List<T> toCheck) where T : GameObject
    {
        var halfAngle = SphereConeHalfAngleDegrees;
        if (halfAngle <= 0f || toCheck == null || toCheck.Count == 0)
            return toCheck;

        return toCheck.Where(o =>
        {
            var degree = Math.Abs(MathUtil.ClampDegAngle(MathUtil.CalculateAngleFrom(origin, o)));
            return degree <= halfAngle;
        }).ToList();
    }
}
