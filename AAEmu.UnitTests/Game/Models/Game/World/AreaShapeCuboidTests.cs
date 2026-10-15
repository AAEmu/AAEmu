using System.Numerics;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.World;

/// <summary>
/// Cuboid plot Area hits must include the box centre — the shared-diagonal fan used to reject it.
/// </summary>
public class AreaShapeCuboidTests
{
    private static uint _nextObjId = 500;

    private static Unit At(float x, float y, float z = 0f)
    {
        var unit = new Unit { ObjId = _nextObjId++ };
        unit.Transform.Local.SetPosition(new Vector3(x, y, z));
        return unit;
    }

    private static Unit Origin(float yawRadians = 0f)
    {
        var unit = new Unit { ObjId = _nextObjId++ };
        unit.Transform.Local.SetPosition(Vector3.Zero);
        unit.Transform.Local.SetZRotation(yawRadians);
        return unit;
    }

    private static AreaShape BoltBox() =>
        new() { Id = 14282, Type = AreaShapeType.Cuboid, Value1 = 2f, Value2 = 2f, Value3 = 10f };

    [Test]
    public async Task CentreOfBox_IsAHit_ForEveryQuarterTurn()
    {
        var shape = BoltBox();
        foreach (var yawDeg in new[] { 0f, 45f, 90f, 135f, 180f, -30f })
        {
            var origin = Origin(yawDeg * MathF.PI / 180f);
            var hits = shape.ComputeCuboid(origin, [At(0f, 0f)]);
            await Assert.That(hits.Count).IsEqualTo(1);
        }
    }

    [Test]
    public async Task OutsideTheBox_IsStillMissed()
    {
        var shape = BoltBox();
        var origin = Origin();
        var hits = shape.ComputeCuboid(origin, [At(3f, 0f), At(0f, 3f), At(5f, 5f)]);
        await Assert.That(hits.Count).IsEqualTo(0);
    }

    [Test]
    public async Task DiagonalNeighbour_CountsAsInside()
    {
        var shape = BoltBox();
        var origin = Origin(MathF.PI / 2f);
        var hits = shape.ComputeCuboid(origin, [At(0.5f, 0.5f), At(1.5f, 0f)]);
        await Assert.That(hits.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ModelSizePad_PullsInANeighbourJustOutsideTheBox()
    {
        var shape = BoltBox();
        var origin = Origin();
        var neighbour = At(2.4f, 0f);
        neighbour.ModelSize = 0.6f;
        var hits = shape.ComputeCuboid(origin, [neighbour, At(3.5f, 0f)]);
        await Assert.That(hits.Count).IsEqualTo(1);
        await Assert.That(hits[0].ObjId).IsEqualTo(neighbour.ObjId);
    }

    [Test]
    public async Task ModelSizePad_HitsOffAxisFaceOverlap()
    {
        // Centre-toward pad used to miss this: body overlaps the right face, but the point pulled
        // toward the box origin stays outside the rectangle.
        var shape = BoltBox();
        var origin = Origin();
        var neighbour = At(2.4f, 1.8f);
        neighbour.ModelSize = 0.45f;
        var hits = shape.ComputeCuboid(origin, [neighbour]);
        await Assert.That(hits.Count).IsEqualTo(1);
        await Assert.That(hits[0].ObjId).IsEqualTo(neighbour.ObjId);
    }
}
