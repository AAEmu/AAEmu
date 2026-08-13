using AAEmu.Game;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

[NotInParallel]
public class DamageEffectCanAttackBypassTests
{
    [Test]
    public async Task PlotSelfTarget_WithoutPredicate_DoesNotBypass()
    {
        var prev = WorldIntegration.AllowsPlotSelfDamageBypass;
        try
        {
            WorldIntegration.AllowsPlotSelfDamageBypass = null;
            var unit = new BaseUnit { ObjId = 100 };
            var plot = new CastPlot(1, 1, 1, 15298);
            await Assert.That(DamageEffect.AllowsCanAttackBypass(plot, unit, unit)).IsFalse();
        }
        finally
        {
            WorldIntegration.AllowsPlotSelfDamageBypass = prev;
        }
    }

    [Test]
    public async Task PlotSelfTarget_AuthorizedPredicate_AllowsBypass()
    {
        var prev = WorldIntegration.AllowsPlotSelfDamageBypass;
        try
        {
            var unit = new BaseUnit { ObjId = 100 };
            WorldIntegration.AllowsPlotSelfDamageBypass = u => u.ObjId == 100;
            var plot = new CastPlot(1, 1, 1, 28457);
            await Assert.That(DamageEffect.AllowsCanAttackBypass(plot, unit, unit)).IsTrue();

            var unrelated = new CastPlot(1, 1, 1, 1);
            WorldIntegration.AllowsPlotSelfDamageBypass = _ => false;
            await Assert.That(DamageEffect.AllowsCanAttackBypass(unrelated, unit, unit)).IsFalse();
        }
        finally
        {
            WorldIntegration.AllowsPlotSelfDamageBypass = prev;
        }
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
        var prev = WorldIntegration.AllowsPlotSelfDamageBypass;
        try
        {
            WorldIntegration.AllowsPlotSelfDamageBypass = _ => true;
            var a = new BaseUnit { ObjId = 1 };
            var b = new BaseUnit { ObjId = 2 };
            var plot = new CastPlot(1, 1, 1, 15298);
            await Assert.That(DamageEffect.AllowsCanAttackBypass(plot, a, b)).IsFalse();
        }
        finally
        {
            WorldIntegration.AllowsPlotSelfDamageBypass = prev;
        }
    }

    [Test]
    public async Task Apply_AuthorizedPlotSelfHit_ReducesHp_UnauthorizedDoesNot()
    {
        var prev = WorldIntegration.AllowsPlotSelfDamageBypass;
        try
        {
            var faction = new SystemFaction { Id = (FactionsEnum)1 };
            var unit = new Unit
            {
                ObjId = 77,
                Faction = faction,
                Hp = 1000,
                MaxHp = 1000
            };
            // Same ObjId + faction ⇒ CanAttack is false (self).
            await Assert.That(unit.CanAttack(unit)).IsFalse();

            var effect = new DamageEffect
            {
                Id = 6844,
                UseFixedDamage = true,
                FixedMin = 50,
                FixedMax = 50,
                Multiplier = 1f
            };
            var plot = new CastPlot(1705, 1, 13818, 28457);
            var casterObj = new SkillCasterUnit(unit.ObjId);
            var targetObj = new SkillCastUnitTarget(unit.ObjId);

            WorldIntegration.AllowsPlotSelfDamageBypass = null;
            effect.Apply(unit, casterObj, unit, targetObj, plot, new EffectSource(), null, DateTime.UtcNow);
            await Assert.That(unit.Hp).IsEqualTo(1000);

            WorldIntegration.AllowsPlotSelfDamageBypass = u => u.ObjId == 77;
            effect.Apply(unit, casterObj, unit, targetObj, plot, new EffectSource(), null, DateTime.UtcNow);
            await Assert.That(unit.Hp).IsEqualTo(950);
        }
        finally
        {
            WorldIntegration.AllowsPlotSelfDamageBypass = prev;
        }
    }
}
