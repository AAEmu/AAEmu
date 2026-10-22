using System.Numerics;

using AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects.SpecialEffects;

public class KnockBackRulesTests
{
    [Test]
    public async Task TryImpulse_PushesAwayFromCaster_WithAuthoredMagnitude()
    {
        var caster = new Vector3(0f, 0f, 0f);
        var target = new Vector3(10f, 0f, 5f);

        var ok = KnockBackRules.TryImpulse(caster, target, 500, verticalImpulse: 0, out var impulse);

        await Assert.That(ok).IsTrue();
        await Assert.That(impulse.X).IsEqualTo(500f).Within(0.01f);
        await Assert.That(impulse.Y).IsEqualTo(0f).Within(0.01f);
        await Assert.That(impulse.Z).IsEqualTo(0f).Within(0.01f);
    }

    [Test]
    public async Task TryImpulse_IsNotAPosition()
    {
        // Continent coordinates must never leak into the vector — that was the launch.
        var caster = new Vector3(21880f, 26570f, 109f);
        var target = new Vector3(21889f, 26580f, 108.6f);

        KnockBackRules.TryImpulse(caster, target, 850, 45, out var impulse);

        await Assert.That(MathF.Abs(impulse.X)).IsLessThan(1000f);
        await Assert.That(MathF.Abs(impulse.Y)).IsLessThan(1000f);
        await Assert.That(impulse.Z).IsEqualTo(45f).Within(0.01f);
    }

    [Test]
    public async Task ImpulseAlong_HorizontalAndVerticalAreIndependent()
    {
        // Dig pack 850/45: full shove on XY, mild lift — not sin(45°)*850.
        var dig = KnockBackRules.ImpulseAlong(1f, 0f, 850, 45);
        await Assert.That(dig.X).IsEqualTo(850f).Within(0.01f);
        await Assert.That(dig.Z).IsEqualTo(45f).Within(0.01f);

        // 1400/90 ships as a strong horizontal with Z=90, not a straight-up rocket.
        var up = KnockBackRules.ImpulseAlong(1f, 0f, 1400, 90);
        await Assert.That(up.X).IsEqualTo(1400f).Within(0.01f);
        await Assert.That(up.Z).IsEqualTo(90f).Within(0.01f);

        // Negative value2 pushes down (400/-20 ships on 10 rows).
        var down = KnockBackRules.ImpulseAlong(1f, 0f, 400, -20);
        await Assert.That(down.X).IsEqualTo(400f).Within(0.01f);
        await Assert.That(down.Z).IsEqualTo(-20f).Within(0.01f);
    }

    [Test]
    public async Task TryImpulse_StackedUnits_ReturnsFalse()
    {
        var p = new Vector3(3f, 4f, 1f);
        await Assert.That(KnockBackRules.TryImpulse(p, p, 500, 20, out _)).IsFalse();
    }

    [Test]
    public async Task DirectionFromYaw_IsUnit()
    {
        var (x, y) = KnockBackRules.DirectionFromYaw(MathF.PI / 2f);
        await Assert.That(x).IsEqualTo(0f).Within(0.001f);
        await Assert.That(y).IsEqualTo(1f).Within(0.001f);
    }

    [Test]
    public async Task TryServerAuthoritySlide_MapsMagnitudeToMetresAlongImpulse()
    {
        var ok = KnockBackRules.TryServerAuthoritySlide(new Vector3(500f, 0f, 171f), 500, out var delta);
        await Assert.That(ok).IsTrue();
        await Assert.That(delta.X).IsEqualTo(0.5f).Within(0.001f);
        await Assert.That(delta.Y).IsEqualTo(0f).Within(0.001f);
        await Assert.That(delta.Z).IsEqualTo(0f).Within(0.001f);
    }
}
