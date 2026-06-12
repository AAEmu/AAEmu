using System.Numerics;
using AAEmu.Game.Models.Game.Skills.Effects;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

public class ImpulseEffectTests
{
    [Test]
    public async Task LocalImpulseToWorldDisplacement_DegenerateDirection_ReturnsUnitScaledImpulse()
    {
        // Caster and target on top of each other → direction degenerate
        // Local impulse 5000 forward (Y) becomes 5m in local space, unchanged in world.
        var impulse = new Vector3(0, 5000, 0);
        var pos = new Vector3(100, 100, 50);

        var result = ImpulseEffect.LocalImpulseToWorldDisplacement(impulse, pos, pos);

        await Assert.That(result).IsEqualTo(new Vector3(0, 5, 0));
    }

    [Test]
    public async Task LocalImpulseToWorldDisplacement_TargetEastOfCaster_ForwardImpulsePushesEast()
    {
        // Caster at origin, target at +X = forward direction is +X
        var impulse = new Vector3(0, 5000, 0); // 5m local "forward"
        var casterPos = new Vector3(0, 0, 0);
        var targetPos = new Vector3(10, 0, 0);

        var result = ImpulseEffect.LocalImpulseToWorldDisplacement(impulse, casterPos, targetPos);

        await Assert.That(MathF.Abs(result.X - 5f)).IsLessThan(0.001f);
        await Assert.That(MathF.Abs(result.Y)).IsLessThan(0.001f);
        await Assert.That(MathF.Abs(result.Z)).IsLessThan(0.001f);
    }

    [Test]
    public async Task LocalImpulseToWorldDisplacement_RightImpulsePerpendicularToForward()
    {
        // Forward = +X. Local "right" (X axis) should map to world -Y per right-hand rule:
        // right = new Vector3(-dir.Y, dir.X, 0) = new Vector3(0, 1, 0) since dir = (1,0,0)
        var impulse = new Vector3(3000, 0, 0); // 3m local "right"
        var casterPos = new Vector3(0, 0, 0);
        var targetPos = new Vector3(10, 0, 0);

        var result = ImpulseEffect.LocalImpulseToWorldDisplacement(impulse, casterPos, targetPos);

        await Assert.That(MathF.Abs(result.X)).IsLessThan(0.001f);
        await Assert.That(MathF.Abs(result.Y - 3f)).IsLessThan(0.001f);
        await Assert.That(MathF.Abs(result.Z)).IsLessThan(0.001f);
    }

    [Test]
    public async Task LocalImpulseToWorldDisplacement_UpImpulseAlwaysAlongWorldZ()
    {
        // Up component should pass through unchanged regardless of forward direction.
        var impulse = new Vector3(0, 0, 4000); // 4m up
        var casterPos = new Vector3(0, 0, 0);
        var targetPos = new Vector3(7, 3, 0); // arbitrary direction

        var result = ImpulseEffect.LocalImpulseToWorldDisplacement(impulse, casterPos, targetPos);

        await Assert.That(MathF.Abs(result.X)).IsLessThan(0.001f);
        await Assert.That(MathF.Abs(result.Y)).IsLessThan(0.001f);
        await Assert.That(MathF.Abs(result.Z - 4f)).IsLessThan(0.001f);
    }

    [Test]
    public async Task LocalImpulseToWorldDisplacement_ZComponentIgnoredInDirectionCalc()
    {
        // Vertical offset between caster and target should not affect the horizontal mapping.
        var impulse = new Vector3(0, 5000, 0);
        var withZ = ImpulseEffect.LocalImpulseToWorldDisplacement(
            impulse, new Vector3(0, 0, 50), new Vector3(10, 0, 0));
        var noZ = ImpulseEffect.LocalImpulseToWorldDisplacement(
            impulse, new Vector3(0, 0, 0), new Vector3(10, 0, 0));

        await Assert.That(withZ).IsEqualTo(noZ);
    }
}
