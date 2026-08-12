using System.Numerics;

using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Utils;

namespace AAEmu.UnitTests.Game.Models.Game.Housing;

/// <summary>
/// The realignment guard decides whether a persisted bound doodad still matches its template offset.
/// Getting it wrong is silent in both directions: too strict and every reconciliation rewrites and
/// re-saves every doodad, too loose and a drifted one is never corrected.
/// </summary>
public class BoundDoodadAlignmentTests
{
    private static WorldSpawnPosition Target(float x = 1f, float y = 2f, float z = 3f,
        float rollDeg = 0f, float pitchDeg = 0f, float yawDeg = 0f) =>
        new() { X = x, Y = y, Z = z, Roll = rollDeg, Pitch = pitchDeg, Yaw = yawDeg };

    private static Vector3 Radians(float rollDeg, float pitchDeg, float yawDeg) =>
        new(rollDeg.DegToRad(), pitchDeg.DegToRad(), yawDeg.DegToRad());

    [Test]
    public async Task Matching_TransformNeedsNoRealignment()
    {
        var target = Target(yawDeg: 90f);

        var needs = BoundDoodadAlignment.NeedsRealignment(
            new Vector3(1f, 2f, 3f), Radians(0f, 0f, 90f), target);

        await Assert.That(needs).IsFalse();
    }

    [Test]
    public async Task PositionDrift_NeedsRealignment()
    {
        var needs = BoundDoodadAlignment.NeedsRealignment(
            new Vector3(1f, 2f, 3.5f), Vector3.Zero, Target());

        await Assert.That(needs).IsTrue();
    }

    [Test]
    public async Task RotationOnlyDrift_NeedsRealignment()
    {
        // The position matches exactly; only the orientation is stale. Comparing position alone would
        // leave this doodad wrong permanently, because the guard would return before applying rotation.
        var needs = BoundDoodadAlignment.NeedsRealignment(
            new Vector3(1f, 2f, 3f), Radians(0f, 0f, 0f), Target(yawDeg: 90f));

        await Assert.That(needs).IsTrue();
    }

    [Test]
    public async Task AngularWraparound_IsTheSameOrientation()
    {
        // 350 and -10 degrees are the same heading; so are 179 and -181.
        var wrapped = BoundDoodadAlignment.NeedsRealignment(
            new Vector3(1f, 2f, 3f), Radians(0f, 0f, 350f), Target(yawDeg: -10f));
        var acrossPi = BoundDoodadAlignment.NeedsRealignment(
            new Vector3(1f, 2f, 3f), Radians(0f, 0f, 179f), Target(yawDeg: -181f));

        await Assert.That(wrapped).IsFalse();
        await Assert.That(acrossPi).IsFalse();
    }

    [Test]
    public async Task FullTurn_IsTheSameOrientation()
    {
        var needs = BoundDoodadAlignment.NeedsRealignment(
            new Vector3(1f, 2f, 3f), Radians(0f, 0f, 360f), Target(yawDeg: 0f));

        await Assert.That(needs).IsFalse();
    }

    [Test]
    public async Task DriftWithinTolerance_IsIgnored()
    {
        var needs = BoundDoodadAlignment.NeedsRealignment(
            new Vector3(1.001f, 2f, 3f), Radians(0f, 0f, 0.01f), Target());

        await Assert.That(needs).IsFalse();
    }

    [Test]
    public async Task EachRotationAxis_IsCompared()
    {
        var roll = BoundDoodadAlignment.NeedsRealignment(new Vector3(1f, 2f, 3f), Radians(45f, 0f, 0f), Target());
        var pitch = BoundDoodadAlignment.NeedsRealignment(new Vector3(1f, 2f, 3f), Radians(0f, 45f, 0f), Target());
        var yaw = BoundDoodadAlignment.NeedsRealignment(new Vector3(1f, 2f, 3f), Radians(0f, 0f, 45f), Target());

        await Assert.That(roll).IsTrue();
        await Assert.That(pitch).IsTrue();
        await Assert.That(yaw).IsTrue();
    }

    [Test]
    public async Task OriginTarget_IsAValidOffset()
    {
        // The origin has to be treated as a real position; "unresolved" is tracked separately, on the
        // binding. A doodad away from an origin target still needs realigning.
        var needs = BoundDoodadAlignment.NeedsRealignment(
            new Vector3(5f, 0f, 0f), Vector3.Zero, Target(0f, 0f, 0f));

        await Assert.That(needs).IsTrue();
    }
}
