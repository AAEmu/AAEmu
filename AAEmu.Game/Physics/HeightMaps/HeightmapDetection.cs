using Jitter2.Collision;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Physics.HeightMaps;

public class HeightmapDetection : IBroadPhaseFilter
{
    private readonly Jitter2.World _world;
    private readonly HeightmapTester _shape;
    private readonly Heightmap _heightmap;
    private readonly ulong _minIndex;

    public HeightmapDetection(Jitter2.World world, HeightmapTester shape)
    {
        _shape = shape;
        _world = world;
        _heightmap = shape.Heightmap;

        (_minIndex, _) = Jitter2.World.RequestId(_heightmap.Width * _heightmap.Height * 2);
    }

    public bool Filter(IDynamicTreeProxy shapeA, IDynamicTreeProxy shapeB)
    {
        if (shapeA != _shape && shapeB != _shape) return true;

        var collider = shapeA == _shape ? shapeB : shapeA;

        if (collider is not RigidBodyShape rbs || rbs.RigidBody.Data.MotionType == MotionType.Static || !rbs.RigidBody.Data.IsActive) return false;

        ref var body = ref rbs.RigidBody!.Data;

        var min = collider.WorldBoundingBox.Min;
        var max = collider.WorldBoundingBox.Max;

        var minX = Math.Max(0, (int)min.X);
        var minY = Math.Max(0, (int)min.Y);
        var maxX = Math.Min(_heightmap.Width - 1, (int)max.X + 1);
        var maxY = Math.Min(_heightmap.Height - 1, (int)max.Y + 1);

        // Height is on Z (world up). Triangles are wound so that (C - A) x (B - A) points up (+Z).
        for (var x = minX; x < maxX; x++)
        {
            for (var y = minY; y < maxY; y++)
            {
                // First triangle of the quad

                var index = 2 * (ulong)(x * _heightmap.Width + y);

                CollisionTriangle triangle;

                triangle.A = new JVector(x + 0, y + 0, _heightmap.GetHeight(x + 0, y + 0));
                triangle.B = new JVector(x + 1, y + 1, _heightmap.GetHeight(x + 1, y + 1));
                triangle.C = new JVector(x + 1, y + 0, _heightmap.GetHeight(x + 1, y + 0));

                var normal = JVector.Normalize((triangle.C - triangle.A) % (triangle.B - triangle.A));

                var hit = NarrowPhase.MprEpa(triangle, rbs, body.Orientation, body.Position, out var pointA, out var pointB, out _, out var penetration);

                if (hit)
                {
                    _world.RegisterContact(rbs.ShapeId, _minIndex + index, _world.NullBody, rbs.RigidBody, pointA, pointB, normal);//, penetration);
                }

                // Second triangle of the quad

                index += 1;
                triangle.A = new JVector(x + 0, y + 0, _heightmap.GetHeight(x + 0, y + 0));
                triangle.B = new JVector(x + 0, y + 1, _heightmap.GetHeight(x + 0, y + 1));
                triangle.C = new JVector(x + 1, y + 1, _heightmap.GetHeight(x + 1, y + 1));

                normal = JVector.Normalize((triangle.C - triangle.A) % (triangle.B - triangle.A));

                hit = NarrowPhase.MprEpa(triangle, rbs, body.Orientation, body.Position, out pointA, out pointB, out _, out penetration);

                if (hit)
                {
                    _world.RegisterContact(rbs.ShapeId, _minIndex + index, _world.NullBody, rbs.RigidBody, pointA, pointB, normal);//, penetration);
                }
            }
        }

        return false;
    }
}
