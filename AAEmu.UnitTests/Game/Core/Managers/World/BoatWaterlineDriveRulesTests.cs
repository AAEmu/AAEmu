using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class BoatWaterlineDriveRulesTests
{
    // ship_models 14 (Ostera / boxship).
    private const float OsteraVelocity = 14.5f;
    private const float OsteraReverseVelocity = 5f;
    private const float OsteraSteerVel = 1f;

    [Test]
    public async Task Stick_IsSbyteOverFullThrow()
    {
        await Assert.That(BoatWaterlineDriveRules.Stick(0)).IsEqualTo(0f);
        await Assert.That(BoatWaterlineDriveRules.Stick(127)).IsEqualTo(1f);
        await Assert.That(BoatWaterlineDriveRules.Stick(-127)).IsEqualTo(-1f);
    }

    [Test]
    public async Task Step_ThrottleZeroStaysPutOnTheSurface()
    {
        var dt = BoatWaterlineDriveRules.DefaultStepSeconds;
        var (x, y, z, yaw, velX, velY) = BoatWaterlineDriveRules.Step(
            13000f, 10200f, 100f, 0f, 0, 0, OsteraVelocity, OsteraSteerVel, dt);
        await Assert.That(x).IsEqualTo(13000f);
        await Assert.That(y).IsEqualTo(10200f);
        await Assert.That(z).IsEqualTo(100f);
        await Assert.That(yaw).IsEqualTo(0f);
        await Assert.That(velX).IsEqualTo(0f);
        await Assert.That(velY).IsEqualTo(0f);
    }

    [Test]
    public async Task Step_FullThrottleAtYawZeroMovesPlusY()
    {
        var dt = BoatWaterlineDriveRules.DefaultStepSeconds;
        var (x, y, z, _, velX, velY) = BoatWaterlineDriveRules.Step(
            0f, 0f, 100f, 0f, 127, 0, OsteraVelocity, OsteraSteerVel, dt);
        await Assert.That(x).IsEqualTo(0f);
        await Assert.That(y).IsEqualTo(OsteraVelocity * dt);
        await Assert.That(z).IsEqualTo(100f);
        await Assert.That(velX).IsEqualTo(0f);
        await Assert.That(velY).IsEqualTo(OsteraVelocity);
    }

    [Test]
    public async Task Step_FullSteerTurnsStarboardFromPlusY()
    {
        var dt = BoatWaterlineDriveRules.DefaultStepSeconds;
        var (_, _, _, yaw, _, _) = BoatWaterlineDriveRules.Step(
            0f, 0f, 100f, 0f, 0, 127, OsteraVelocity, OsteraSteerVel, dt);
        await Assert.That(yaw).IsEqualTo(BoatWaterlineDriveRules.SteerYawSign * OsteraSteerVel * dt);
    }

    [Test]
    public async Task CruiseSpeed_ForwardUsesVelocity_ReverseUsesReverseColumn()
    {
        await Assert.That(BoatWaterlineDriveRules.CruiseSpeed(127, OsteraVelocity, OsteraReverseVelocity))
            .IsEqualTo(OsteraVelocity);
        await Assert.That(BoatWaterlineDriveRules.CruiseSpeed(-127, OsteraVelocity, OsteraReverseVelocity))
            .IsEqualTo(OsteraReverseVelocity);
        await Assert.That(BoatWaterlineDriveRules.CruiseSpeed(-127, OsteraVelocity, 0f))
            .IsEqualTo(OsteraVelocity);
    }

    [Test]
    public async Task ClampStepSeconds_CapsAtAFewHelmPeriods()
    {
        await Assert.That(BoatWaterlineDriveRules.ClampStepSeconds(0f))
            .IsEqualTo(BoatWaterlineDriveRules.DefaultStepSeconds);
        await Assert.That(BoatWaterlineDriveRules.ClampStepSeconds(-1f))
            .IsEqualTo(BoatWaterlineDriveRules.DefaultStepSeconds);
        await Assert.That(BoatWaterlineDriveRules.ClampStepSeconds(5f))
            .IsEqualTo(BoatWaterlineDriveRules.MaxStepSeconds);
        await Assert.That(BoatWaterlineDriveRules.ClampStepSeconds(0.1f)).IsEqualTo(0.1f);
    }

    [Test]
    public async Task RotationShortsFromYaw_RoundTripsThroughTransformQuat()
    {
        foreach (var yaw in new[] { 0f, 0.5f, -1.2f, MathF.PI * 0.5f })
        {
            var (rx, ry, rz) = BoatWaterlineDriveRules.RotationShortsFromYaw(yaw);
            var q = BoatSeamHandoffRules.QuatFromRotationShorts(rx, ry, rz);
            var back = PositionAndRotation.FromQuaternion(q).Z;
            await Assert.That(MathF.Abs(back - yaw)).IsLessThan(0.02f);
        }
    }
}
