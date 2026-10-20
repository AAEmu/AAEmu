using System.Numerics;

using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Utils;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class LinearMoveFacingRulesTests
{
    [Test]
    public async Task HeadingAlongFacing_ForwardMatchesAddDistanceToFront()
    {
        // The heading handed to MathUtil's (cos, sin) helper must land where PositionAndRotation's front
        // walk lands, for any yaw.
        foreach (var yawDeg in new[] { 0f, 37f, 90f, 180f, -120f })
        {
            var yaw = yawDeg.DegToRad();
            var front = new PositionAndRotation { Rotation = new Vector3(0f, 0f, yaw) };
            front.AddDistanceToFront(5f);

            var (x, y) = MathUtil.AddDistanceToFront(5f, 0f, 0f, LinearMoveFacingRules.HeadingAlongFacing(yaw, 5f));
            await Assert.That(x).IsEqualTo(front.Position.X).Within(1e-3f);
            await Assert.That(y).IsEqualTo(front.Position.Y).Within(1e-3f);
        }
    }

    [Test]
    public async Task HeadingAlongFacing_NegativeDistanceIsTheOppositeWay()
    {
        var yaw = 37f.DegToRad();
        var (fx, fy) = MathUtil.AddDistanceToFront(5f, 0f, 0f, LinearMoveFacingRules.HeadingAlongFacing(yaw, 5f));
        var (bx, by) = MathUtil.AddDistanceToFront(5f, 0f, 0f, LinearMoveFacingRules.HeadingAlongFacing(yaw, -5f));
        await Assert.That(bx).IsEqualTo(-fx).Within(1e-3f);
        await Assert.That(by).IsEqualTo(-fy).Within(1e-3f);
    }

    [Test]
    public async Task LeapRunsAlongFacing_SelfOrCoincidentOnly()
    {
        var at = new Vector3(10f, 10f, 0f);
        await Assert.That(LinearMoveFacingRules.LeapRunsAlongFacing(1, 1, at, new Vector3(30f, 10f, 0f))).IsTrue();
        await Assert.That(LinearMoveFacingRules.LeapRunsAlongFacing(1, 2, at, at)).IsTrue();
        await Assert.That(LinearMoveFacingRules.LeapRunsAlongFacing(1, 2, at, new Vector3(30f, 10f, 0f))).IsFalse();
    }

    [Test]
    public async Task TurnsIntoTravel_OnlyForwardRuns()
    {
        await Assert.That(LinearMoveFacingRules.TurnsIntoTravel(6f)).IsTrue();
        await Assert.That(LinearMoveFacingRules.TurnsIntoTravel(0f)).IsTrue();
        await Assert.That(LinearMoveFacingRules.TurnsIntoTravel(-5f)).IsFalse();
    }
}
