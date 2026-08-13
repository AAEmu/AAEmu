using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

public class DamageEffectCanAttackBypassTests
{
    [Test]
    public async Task PlotSelfTarget_AllowsBypass()
    {
        var unit = new BaseUnit { ObjId = 100 };
        var plot = new CastPlot(1, 1, 1, 15298);
        await Assert.That(DamageEffect.AllowsCanAttackBypass(plot, unit, unit)).IsTrue();
    }

    [Test]
    public async Task SkillSelfTarget_DoesNotBypass()
    {
        var unit = new BaseUnit { ObjId = 100 };
        var skill = new CastSkill(15298, 1);
        await Assert.That(DamageEffect.AllowsCanAttackBypass(skill, unit, unit)).IsFalse();
    }

    [Test]
    public async Task PlotDifferentTargets_DoesNotBypass()
    {
        var a = new BaseUnit { ObjId = 1 };
        var b = new BaseUnit { ObjId = 2 };
        var plot = new CastPlot(1, 1, 1, 15298);
        await Assert.That(DamageEffect.AllowsCanAttackBypass(plot, a, b)).IsFalse();
    }
}
