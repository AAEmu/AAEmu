using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Plots;

public class PlotCastOriginStandRulesTests
{
    [Test]
    public async Task CasterBasis_AfterSoftMove_UsesFrozenMark()
    {
        var caster = new Unit { ObjId = 10 };
        caster.Transform.Local.SetPosition(999f, 888f, 77f);
        var skill = new Skill();
        skill.RecordCastOriginForTests(new SavedPosition(186, 1, 100f, 200f, 30f, 1.5f));
        skill.NoteCasterSoftMoved();
        var state = new PlotState(caster, null, caster, null, null, skill);

        var ok = PlotCastOriginStandRules.TryGetFrozenCasterOrigin(
            caster, state, out var x, out var y, out var z, out var yaw);

        await Assert.That(ok).IsTrue();
        await Assert.That(x).IsEqualTo(100f);
        await Assert.That(y).IsEqualTo(200f);
        await Assert.That(z).IsEqualTo(30f);
        await Assert.That(yaw).IsEqualTo(1.5f);
    }

    [Test]
    public async Task CasterBasis_WithoutSoftMove_KeepsLiveTransform()
    {
        // CastOrigin is set on every Use; leap Area stands must not pin to take-off without a soft move.
        var caster = new Unit { ObjId = 10 };
        var skill = new Skill();
        skill.RecordCastOriginForTests(new SavedPosition(186, 1, 100f, 200f, 30f, 0f));
        var state = new PlotState(caster, null, caster, null, null, skill);

        var ok = PlotCastOriginStandRules.TryGetFrozenCasterOrigin(
            caster, state, out _, out _, out _, out _);

        await Assert.That(ok).IsFalse();
    }

    [Test]
    public async Task OtherUnitBasis_KeepsLiveTransform()
    {
        var caster = new Unit { ObjId = 10 };
        var other = new Unit { ObjId = 11 };
        var skill = new Skill();
        skill.RecordCastOriginForTests(new SavedPosition(186, 1, 100f, 200f, 30f, 0f));
        skill.NoteCasterSoftMoved();
        var state = new PlotState(caster, null, caster, null, null, skill);

        var ok = PlotCastOriginStandRules.TryGetFrozenCasterOrigin(
            other, state, out _, out _, out _, out _);

        await Assert.That(ok).IsFalse();
    }

    [Test]
    public async Task NoCastOrigin_KeepsLiveTransform()
    {
        var caster = new Unit { ObjId = 10 };
        var state = new PlotState(caster, null, caster, null, null, new Skill());

        var ok = PlotCastOriginStandRules.TryGetFrozenCasterOrigin(
            caster, state, out _, out _, out _, out _);

        await Assert.That(ok).IsFalse();
    }
}
