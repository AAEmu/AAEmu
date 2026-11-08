using System.Numerics;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Utils;
using Jitter2.LinearMath;
using GameTransform = AAEmu.Game.Models.Game.World.Transform.Transform;

namespace AAEmu.UnitTests.Game.Models.Game.World.Transform;

/// <summary>
/// Tests for composing child positions and rotations through a parent Transform.
/// </summary>
public class TransformTests
{
    [Test]
    public async Task World_ChildOfYawedParent_OffsetRotatesWithParent()
    {
        // Arrange: parent faces yaw 90 degrees, child sits 2 m in front of it (+Y local)
        using var parent = new GameTransform(null, null, 100f, 200f, 10f, 0f, 0f, 90f.DegToRad());
        using var child = new GameTransform(null, parent, 0f, 2f, 0f);

        // Act
        var world = child.World;

        // Assert: front of a yaw-90 parent is -X
        await Assert.That(world.Position.X).IsEqualTo(98f).Within(0.0001f);
        await Assert.That(world.Position.Y).IsEqualTo(200f).Within(0.0001f);
        await Assert.That(world.Position.Z).IsEqualTo(10f).Within(0.0001f);
        await Assert.That(world.Rotation.Z).IsEqualTo(90f.DegToRad()).Within(0.0001f);
    }

    [Test]
    public async Task World_ChildOfTiltedParent_MatchesQuaternionMath()
    {
        // Arrange: parent with roll, pitch and yaw; child offset on all axes
        var parentRotation = new JVector(0.2f, -0.1f, 1.1f);
        var childOffset = new Vector3(1.5f, -2f, 0.5f);
        using var parent = new GameTransform(null, null, 10f, 20f, 30f, parentRotation.X, parentRotation.Y, parentRotation.Z);
        using var child = new GameTransform(null, parent, childOffset.X, childOffset.Y, childOffset.Z);

        // Act
        var world = child.World;

        // Assert: same as rotating the offset with System.Numerics
        var expected = new Vector3(10f, 20f, 30f) + Vector3.Transform(childOffset, PositionAndRotation.ToQuaternion(parentRotation));
        await Assert.That(world.Position.X).IsEqualTo(expected.X).Within(0.0001f);
        await Assert.That(world.Position.Y).IsEqualTo(expected.Y).Within(0.0001f);
        await Assert.That(world.Position.Z).IsEqualTo(expected.Z).Within(0.0001f);
    }

    [Test]
    public async Task Equals_WorldSpawnPosition_MatchesJVectorAndVector3()
    {
        // Arrange
        var wsp = new WorldSpawnPosition { X = 1f, Y = 2f, Z = 3f };

        // Act & Assert
        await Assert.That(wsp.Equals(new JVector(1f, 2f, 3f))).IsTrue();
        await Assert.That(wsp.Equals(new Vector3(1f, 2f, 3f))).IsTrue();
        await Assert.That(wsp.Equals(new JVector(1f, 2f, 4f))).IsFalse();
    }
}
