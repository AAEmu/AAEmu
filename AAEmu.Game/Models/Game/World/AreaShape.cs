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
    /// Sphere cone half-angle in degrees. On <see cref="AreaShapeType.Sphere"/>, content stores
    /// radius in <see cref="Value1"/> and this half-angle in <see cref="Value3"/> (e.g. shotgun
    /// aoe_shapes 20454 / 20458: 15.7m, ±45°). Cuboid uses Value3 as vertical extent instead.
    /// </summary>
    public float SphereConeHalfAngleDegrees => Type == AreaShapeType.Sphere ? Value3 : 0f;

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

            var tri2 = MathUtil.PointInTriangle((o.Transform.World.Position.X, o.Transform.World.Position.Y), vertices[1], vertices[2],
                vertices[3]);

            return tri1 || tri2;
        }).ToList();

        return toCheck;
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
