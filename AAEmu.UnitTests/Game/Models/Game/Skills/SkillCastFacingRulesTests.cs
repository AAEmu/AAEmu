using System.Numerics;

using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillCastFacingRulesTests
{
    [Test]
    public async Task YawToward_PlusY_IsZero()
    {
        await Assert.That(SkillCastFacingRules.TryGetYawToward(
            Vector3.Zero, new Vector3(0f, 10f, 0f), out var yaw)).IsTrue();
        await Assert.That(MathF.Abs(yaw)).IsLessThan(1e-4f);
        var stand = new PositionAndRotation { Rotation = new Vector3(0f, 0f, yaw) };
        stand.AddDistanceToFront(10f);
        await Assert.That(MathF.Abs(stand.Position.X)).IsLessThan(1e-3f);
        await Assert.That(stand.Position.Y).IsEqualTo(10f).Within(1e-3f);
    }

    [Test]
    public async Task YawToward_PlusX_MatchesAddDistanceToFront()
    {
        await Assert.That(SkillCastFacingRules.TryGetYawToward(
            Vector3.Zero, new Vector3(10f, 0f, 0f), out var yaw)).IsTrue();
        var stand = new PositionAndRotation { Rotation = new Vector3(0f, 0f, yaw) };
        stand.AddDistanceToFront(10f);
        await Assert.That(stand.Position.X).IsEqualTo(10f).Within(1e-3f);
        await Assert.That(MathF.Abs(stand.Position.Y)).IsLessThan(1e-3f);
    }

    [Test]
    public async Task YawToward_Coincident_Fails()
    {
        await Assert.That(SkillCastFacingRules.TryGetYawToward(
            new Vector3(1f, 2f, 3f), new Vector3(1f, 2f, 9f), out _)).IsFalse();
    }

    [Test]
    public async Task InputDirection_RoundTripsThroughYaw()
    {
        // Headings where a 127-step encode drops a step (80, 100) must come back as the aim byte.
        foreach (var wire in new byte[] { 0, 32, 80, 100 })
        {
            var yaw = SkillCastFacingRules.YawFromInputDirection(wire);
            var echoed = SkillCastFacingRules.InputDirectionFromYaw(yaw);
            var echoedYaw = SkillCastFacingRules.YawFromInputDirection(echoed);
            await Assert.That(Math.Abs(NormalizeAngle(yaw - echoedYaw))).IsLessThan(0.001f);
        }
    }

    [Test]
    public async Task Resolve_PrefersGroundAimOverInputDirection()
    {
        await Assert.That(SkillCastFacingRules.TryResolveCastYaw(
            Vector3.Zero,
            new Vector3(10f, 0f, 0f),
            inputDirection: 0,
            useInputDirection: true,
            out var yaw)).IsTrue();
        var stand = new PositionAndRotation { Rotation = new Vector3(0f, 0f, yaw) };
        stand.AddDistanceToFront(10f);
        await Assert.That(stand.Position.X).IsEqualTo(10f).Within(1e-3f);
    }

    [Test]
    public async Task Resolve_UsesInputDirectionOnlyWhenFlaggedAndNoAim()
    {
        await Assert.That(SkillCastFacingRules.TryResolveCastYaw(
            Vector3.Zero,
            aimWorldPos: null,
            inputDirection: 0,
            useInputDirection: true,
            out var yaw)).IsTrue();
        // Look byte 0 is movement yaw 0: the same conversion a move packet gets, front = +Y.
        await Assert.That(MathF.Abs(NormalizeAngle(yaw))).IsLessThan(1e-3f);
    }

    [Test]
    public async Task Resolve_IgnoresInputDirectionWhenSkillDoesNotUseIt()
    {
        // Sonic Wave / Ghastly Pack ship use_input_direction 'f' and the client sends the byte as 0 there;
        // planting it would aim every such cast at world +Y. Body facing stands.
        await Assert.That(SkillCastFacingRules.TryResolveCastYaw(
            Vector3.Zero,
            aimWorldPos: null,
            inputDirection: 0,
            useInputDirection: false,
            out _)).IsFalse();
    }

    [Test]
    public async Task Resolve_GroundAimStillWorksWhenInputDirectionUnused()
    {
        await Assert.That(SkillCastFacingRules.TryResolveCastYaw(
            Vector3.Zero,
            new Vector3(10f, 0f, 0f),
            inputDirection: 5,
            useInputDirection: false,
            out var yaw)).IsTrue();
        var stand = new PositionAndRotation { Rotation = new Vector3(0f, 0f, yaw) };
        stand.AddDistanceToFront(10f);
        await Assert.That(stand.Position.X).IsEqualTo(10f).Within(1e-3f);
    }

    [Test]
    public async Task AimFromUnitTarget_IsOff()
    {
        // Tab-select must not redirect a frontal plot off the body facing.
        await Assert.That(SkillCastFacingRules.AimFromUnitTarget).IsFalse();
    }

    [Test]
    public async Task LocalYawFromWorldYaw_SubtractsParentYaw()
    {
        // Parent turned +90° (π/2); desired world yaw 0 → local must be −90° so World composes to 0.
        var parentYaw = MathF.PI / 2f;
        var parentQuat = PositionAndRotation.ToQuaternion(new Vector3(0f, 0f, parentYaw));
        var local = SkillCastFacingRules.LocalYawFromWorldYaw(parentQuat, worldYawRadians: 0f);
        await Assert.That(NormalizeAngle(local + parentYaw)).IsEqualTo(0f).Within(1e-3f);

        // Unrotated parent: local == world.
        var identity = PositionAndRotation.ToQuaternion(Vector3.Zero);
        var same = SkillCastFacingRules.LocalYawFromWorldYaw(identity, worldYawRadians: parentYaw);
        await Assert.That(NormalizeAngle(same - parentYaw)).IsEqualTo(0f).Within(1e-3f);
    }

    private static float NormalizeAngle(float radians)
    {
        var turn = (float)(Math.PI * 2);
        radians %= turn;
        if (radians > Math.PI)
            radians -= turn;
        if (radians < -Math.PI)
            radians += turn;
        return radians;
    }
}
