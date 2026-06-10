using System.Numerics;
using AAEmu.Game.Models.Game.Skills.Effects;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

public class PhysicalExplosionEffectTests
{
    [Test]
    public async Task ComputeKnockDisplacement_OutsideRadius_ReturnsNull()
    {
        var caster = new Vector3(0, 0, 0);
        var target = new Vector3(20, 0, 0); // 20m away, radius 10m

        var result = PhysicalExplosionEffect.ComputeKnockDisplacement(
            caster, target, radius: 10f, holeSize: 1f, pressure: 5000f);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ComputeKnockDisplacement_InsideHoleSize_ReturnsNull()
    {
        var caster = new Vector3(0, 0, 0);
        var target = new Vector3(0.5f, 0, 0); // 0.5m away, hole 1m

        var result = PhysicalExplosionEffect.ComputeKnockDisplacement(
            caster, target, radius: 10f, holeSize: 1f, pressure: 5000f);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ComputeKnockDisplacement_BelowMinPerceptibleKnock_ReturnsNull()
    {
        var caster = new Vector3(0, 0, 0);
        var target = new Vector3(9.5f, 0, 0); // very near edge → tiny falloff

        // Pressure 100 / 1000 = 0.1m base × small falloff → < 0.5m → null
        var result = PhysicalExplosionEffect.ComputeKnockDisplacement(
            caster, target, radius: 10f, holeSize: 1f, pressure: 100f);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ComputeKnockDisplacement_AtFiveMetres_KnocksOutwardAlongPushDirection()
    {
        var caster = new Vector3(0, 0, 0);
        var target = new Vector3(5, 0, 0); // 5m along +X

        // radius 10, hole 1, pressure 5000 → base 5m × falloff (1 - 5/10) = 0.5 → 2.5m
        var result = PhysicalExplosionEffect.ComputeKnockDisplacement(
            caster, target, radius: 10f, holeSize: 1f, pressure: 5000f);

        await Assert.That(result).IsNotNull();
        var d = result!.Value;
        // Direction should be along +X (push away from caster)
        await Assert.That(d.X).IsGreaterThan(0f);
        await Assert.That(MathF.Abs(d.Y)).IsLessThan(0.001f);
        await Assert.That(MathF.Abs(d.Length() - 2.5f)).IsLessThan(0.01f);
    }

    [Test]
    public async Task ComputeKnockDisplacement_FalloffScalesLinearlyWithDistance()
    {
        // Near (1m, half hole + 0): falloff 1 - 1/10 = 0.9 → 0.9 * 5 = 4.5m
        var near = PhysicalExplosionEffect.ComputeKnockDisplacement(
            new Vector3(0, 0, 0), new Vector3(1, 0, 0), radius: 10f, holeSize: 0.5f, pressure: 5000f);
        // Far (9m): falloff 1 - 9/10 = 0.1 → 0.5m
        var far = PhysicalExplosionEffect.ComputeKnockDisplacement(
            new Vector3(0, 0, 0), new Vector3(9, 0, 0), radius: 10f, holeSize: 0.5f, pressure: 5000f);

        await Assert.That(near).IsNotNull();
        await Assert.That(far).IsNotNull();
        await Assert.That(near!.Value.Length()).IsGreaterThan(far!.Value.Length());
    }
}
