using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.World;
public class AreaShape
{
    public uint Id { get; set; }
    public AreaShapeType Type { get; set; }
    public float Value1 { get; set; }
    public float Value2 { get; set; }
    public float Value3 { get; set; }

    public List<T> ComputeCuboid<T>(GameObject origin, List<T> toCheck) where T : GameObject
    {
        // Z check
        var zOffset = Value3;
        toCheck = toCheck.Where(o => (o.Transform.World.Position.Z >= origin.Transform.World.Position.Z - zOffset) && (o.Transform.World.Position.Z <= origin.Transform.World.Position.Z + zOffset)).ToList();
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
}
