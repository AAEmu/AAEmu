using System.Numerics;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.World;

/// <summary>
/// aoe_shapes.value3 on a sphere is the full sweep of the cone, so the filter's half-angle is value3 / 2.
/// </summary>
/// <remarks>
/// Settled from the shipped data rather than from a guess: value3 takes the values 180 (6 sphere rows) and
/// 200 (2 rows, both on 칼릴의 검 44784/47828) beside the 108 rows that spell "omni" as 360. A half-angle
/// reading cannot explain 180 without making it a second spelling of 360, and cannot explain 200 at all.
/// </remarks>
public class AreaShapeConeTests
{
    private static uint _nextObjId = 100;

    private static AreaShape Cone(float sweepDegrees) =>
        new() { Id = 1, Type = AreaShapeType.Sphere, Value1 = 30f, Value3 = sweepDegrees };

    /// <summary>A unit standing <paramref name="bearingDegrees"/> off the origin's facing at 10 m.</summary>
    private static Unit AtBearing(float bearingDegrees)
    {
        var unit = new Unit { ObjId = _nextObjId++ };
        var rad = bearingDegrees * MathF.PI / 180f;
        // PositionAndRotation.AddDistanceToFront puts a point at (-d*sin(yaw), d*cos(yaw)), and
        // MathUtil.CalculateAngleFrom subtracts that yaw, so placing by the bearing directly is exact.
        unit.Transform.Local.SetPosition(new Vector3(-10f * MathF.Sin(rad), 10f * MathF.Cos(rad), 0f));
        return unit;
    }

    private static Unit Origin()
    {
        var unit = new Unit { ObjId = _nextObjId++ };
        unit.Transform.Local.SetPosition(Vector3.Zero);
        unit.Transform.Local.SetZRotation(0f);
        return unit;
    }

    [Test]
    public async Task HalfAngle_IsHalfTheShippedSweep()
    {
        // Every distinct non-zero value3 the 323 cone rows of the 18,582 sphere rows use.
        float[] shippedSweeps =
        [
            0.3f, 0.5f, 1f, 1.5f, 2f, 2.5f, 3f, 4f, 5f, 15f, 20f, 25f, 30f, 35f, 40f, 45f, 46f, 50f,
            53f, 60f, 70f, 80f, 90f, 120f, 180f, 200f, 360f
        ];

        foreach (var sweep in shippedSweeps)
        {
            var shape = Cone(sweep);
            await Assert.That(shape.SphereConeHalfAngleDegrees).IsEqualTo(sweep / 2f);
            // A half-angle above 180° is not a bearing range; the old reading produced one for 200.
            await Assert.That(shape.SphereConeHalfAngleDegrees).IsLessThanOrEqualTo(180f);
        }
    }

    [Test]
    public async Task PlainSphere_HasNoConeFilter()
    {
        var shape = Cone(0f);
        await Assert.That(shape.SphereConeHalfAngleDegrees).IsEqualTo(0f);

        var cuboid = new AreaShape { Type = AreaShapeType.Cuboid, Value1 = 3f, Value2 = 5f, Value3 = 20f };
        await Assert.That(cuboid.SphereConeHalfAngleDegrees).IsEqualTo(0f);
    }

    [Test]
    public async Task NinetyDegreeSweep_KeepsInsideTheConeAndDropsBehind()
    {
        // value3 90 is the quarter-circle row (20 sphere rows); half sweep is 45.
        var shape = Cone(90f);
        var origin = Origin();

        var inside = shape.FilterSphereCone(origin, [AtBearing(40f)]);
        await Assert.That(inside.Count).IsEqualTo(1);

        var atEdge = shape.FilterSphereCone(origin, [AtBearing(45f)]);
        await Assert.That(atEdge.Count).IsEqualTo(1);

        var outside = shape.FilterSphereCone(origin, [AtBearing(50f)]);
        await Assert.That(outside.Count).IsEqualTo(0);

        var behind = shape.FilterSphereCone(origin, [AtBearing(180f)]);
        await Assert.That(behind.Count).IsEqualTo(0);
    }

    [Test]
    public async Task TwoHundredDegreeSweep_IsAnArcAndNotAnOmniCone()
    {
        // Shapes 18214 and 21872 (칼릴의 검 44784/47828). Read as a half-angle this shape accepted every
        // bearing; as the full sweep it accepts ±100.
        var shape = Cone(200f);
        var origin = Origin();

        await Assert.That(shape.FilterSphereCone(origin, [AtBearing(95f)]).Count).IsEqualTo(1);
        await Assert.That(shape.FilterSphereCone(origin, [AtBearing(110f)]).Count).IsEqualTo(0);
        await Assert.That(shape.FilterSphereCone(origin, [AtBearing(179f)]).Count).IsEqualTo(0);
    }

    [Test]
    public async Task ThreeSixtySweep_IsOmni()
    {
        var shape = Cone(360f);
        var origin = Origin();

        await Assert.That(shape.SphereConeHalfAngleDegrees).IsEqualTo(180f);
        await Assert.That(shape.FilterSphereCone(origin, [AtBearing(0f), AtBearing(90f), AtBearing(179f)])
            .Count).IsEqualTo(3);
    }

    [Test]
    public async Task HalfCircleSweep_KeepsTheFrontHalfOnly()
    {
        // value3 180 (6 sphere rows, e.g. 19121 on 맹독 45493).
        var shape = Cone(180f);
        var origin = Origin();

        await Assert.That(shape.FilterSphereCone(origin, [AtBearing(89f)]).Count).IsEqualTo(1);
        await Assert.That(shape.FilterSphereCone(origin, [AtBearing(91f)]).Count).IsEqualTo(0);
    }
}
