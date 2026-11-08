using System.Numerics;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Physics.Util;
using AAEmu.Game.Utils;
using Jitter2.LinearMath;

namespace AAEmu.UnitTests.Game.Physics.Util;

/// <summary>
/// Tests for PhysicsUtil: physics orientations use the game's axes (Z up), so they convert to game rotation directly.
/// </summary>
public class PhysicsUtilTests
{
    [Test]
    [Arguments(0f)]
    [Arguments(30f)]
    [Arguments(90f)]
    [Arguments(-135f)]
    [Arguments(179f)]
    public async Task GetYaw_WithRotationAboutZ_ReturnsGameYaw(float yawDeg)
    {
        // Arrange
        var yaw = yawDeg.DegToRad();
        var orientation = JQuaternion.CreateRotationZ(yaw);

        // Act
        var result = PhysicsUtil.GetYaw(orientation);

        // Assert
        await Assert.That(result).IsEqualTo(yaw).Within(0.0001f);
    }

    [Test]
    public async Task GetGameRotation_WithGameQuaternion_RoundTripsRollPitchYaw()
    {
        // Arrange
        var rotation = new Vector3(0.1f, -0.05f, 1.2f);
        var q = PositionAndRotation.ToQuaternion(rotation);
        var orientation = new JQuaternion(q.X, q.Y, q.Z, q.W);

        // Act
        var result = PhysicsUtil.GetGameRotation(orientation);

        // Assert
        await Assert.That(result.X).IsEqualTo(rotation.X).Within(0.0001f);
        await Assert.That(result.Y).IsEqualTo(rotation.Y).Within(0.0001f);
        await Assert.That(result.Z).IsEqualTo(rotation.Z).Within(0.0001f);
    }

    [Test]
    [Arguments(0f)]
    [Arguments(45f)]
    [Arguments(-120f)]
    public async Task GetForward_WithYaw_MatchesTransformAddDistanceToFront(float yawDeg)
    {
        // Arrange
        var transform = new PositionAndRotation();
        transform.SetRotation(0f, 0f, yawDeg.DegToRad());
        transform.AddDistanceToFront(1f);

        // Act
        var (x, y) = PhysicsUtil.GetForward(yawDeg.DegToRad());

        // Assert
        await Assert.That(x).IsEqualTo(transform.Position.X).Within(0.0001f);
        await Assert.That(y).IsEqualTo(transform.Position.Y).Within(0.0001f);
    }
}
