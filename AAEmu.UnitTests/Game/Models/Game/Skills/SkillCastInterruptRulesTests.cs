using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillCastInterruptRulesTests
{
    [Test]
    public async Task DamagePercent_IsAShareOfMaximumHealth()
    {
        await Assert.That(SkillCastInterruptRules.DamagePercent(250, 1000)).IsEqualTo(25d);
        // A unit with no maximum health cannot be measured against, so nothing is a big hit.
        await Assert.That(SkillCastInterruptRules.DamagePercent(250, 0)).IsEqualTo(0d);
    }

    [Test]
    public async Task BigHit_IsOneWholeTenPercentUnit()
    {
        // 10% is the granularity formulas 2 counts damage in.
        await Assert.That(SkillCastInterruptRules.IsBigHit(9.9)).IsFalse();
        await Assert.That(SkillCastInterruptRules.IsBigHit(10)).IsTrue();
        await Assert.That(SkillCastInterruptRules.IsBigHit(80)).IsTrue();
    }

    [Test]
    public async Task CancelPercent_IsClampedAndZeroWhenTheFormulaIsMissing()
    {
        await Assert.That(SkillCastInterruptRules.CancelPercent(null)).IsEqualTo(0d);
        await Assert.That(SkillCastInterruptRules.CancelPercent(-5)).IsEqualTo(0d);
        await Assert.That(SkillCastInterruptRules.CancelPercent(30)).IsEqualTo(30d);
        await Assert.That(SkillCastInterruptRules.CancelPercent(500)).IsEqualTo(100d);
    }

    [Test]
    public async Task Delay_IsClampedAndNeverNegative()
    {
        await Assert.That(SkillCastInterruptRules.DelayMilliseconds(null)).IsEqualTo(0);
        await Assert.That(SkillCastInterruptRules.DelayMilliseconds(-300)).IsEqualTo(0);
        await Assert.That(SkillCastInterruptRules.DelayMilliseconds(630)).IsEqualTo(630);
        await Assert.That(SkillCastInterruptRules.DelayMilliseconds(90000))
            .IsEqualTo(SkillCastInterruptRules.MaximumDelayMilliseconds);
    }

    [Test]
    public async Task BigHit_CancelsWhenTheSkillSaysSo()
    {
        var decision = SkillCastInterruptRules.Decide(
            stopCastingOnBigHit: true, castingCancelable: true, castingDelayable: true,
            damagePercent: 25, cancelPercent: 0, delayMs: 630, rollPercent: 99);

        await Assert.That(decision.Cancel).IsTrue();
        await Assert.That(decision.DelayMilliseconds).IsEqualTo(0);
    }

    [Test]
    public async Task BigHit_DoesNotBreakAnUncancelableCast()
    {
        // casting_cancelable 'f': the hit cannot break it, so it is pushed back instead.
        var decision = SkillCastInterruptRules.Decide(
            stopCastingOnBigHit: true, castingCancelable: false, castingDelayable: true,
            damagePercent: 25, cancelPercent: 0, delayMs: 630, rollPercent: 99);

        await Assert.That(decision.Cancel).IsFalse();
        await Assert.That(decision.DelayMilliseconds).IsEqualTo(630);
    }

    [Test]
    public async Task SmallHit_DelaysByTheFormulaValue()
    {
        // 8% of maximum health is under the big-hit threshold: floor(8/5) = 1, 1 * 126 = 126 ms.
        var decision = SkillCastInterruptRules.Decide(
            stopCastingOnBigHit: true, castingCancelable: true, castingDelayable: true,
            damagePercent: 8, cancelPercent: 0, delayMs: 126, rollPercent: 99);

        await Assert.That(decision.Cancel).IsFalse();
        await Assert.That(decision.DelayMilliseconds).IsEqualTo(126);
    }

    [Test]
    public async Task CancelFormula_TakesEffectWhenTheRowIsNotZeroed()
    {
        // A server that edits formulas 2 gets a real cancel chance.
        var decision = SkillCastInterruptRules.Decide(
            stopCastingOnBigHit: false, castingCancelable: true, castingDelayable: true,
            damagePercent: 15, cancelPercent: 40, delayMs: 378, rollPercent: 12);

        await Assert.That(decision.Cancel).IsTrue();

        var survives = SkillCastInterruptRules.Decide(
            stopCastingOnBigHit: false, castingCancelable: true, castingDelayable: true,
            damagePercent: 15, cancelPercent: 40, delayMs: 378, rollPercent: 87);
        await Assert.That(survives.Cancel).IsFalse();
        await Assert.That(survives.DelayMilliseconds).IsEqualTo(378);
    }

    [Test]
    public async Task ShippedContent_NeverCancelsFromDamage()
    {
        // formulas 2 is 0 * (...) * (...): zero for every hit, so Decide only ever delays.
        for (var damagePercent = 1d; damagePercent <= 100d; damagePercent += 5d)
        {
            var cancelPercent = SkillCastInterruptRules.CancelPercent(0d);
            var decision = SkillCastInterruptRules.Decide(
                stopCastingOnBigHit: false, castingCancelable: true, castingDelayable: true,
                damagePercent, cancelPercent, delayMs: 126 * (int)(damagePercent / 5), rollPercent: 0);

            await Assert.That(decision.Cancel).IsFalse();
        }
    }

    [Test]
    public async Task UncancelableAndUndelayableCast_IgnoresTheHit()
    {
        var decision = SkillCastInterruptRules.Decide(
            stopCastingOnBigHit: false, castingCancelable: false, castingDelayable: false,
            damagePercent: 90, cancelPercent: 100, delayMs: 5000, rollPercent: 0);

        await Assert.That(decision).IsEqualTo(SkillCastInterruptRules.NoInterrupt);
    }

    [Test]
    public async Task FormulaText_FromTheContentDb_EvaluatesToTheHandComputedValues()
    {
        // The two expressions exactly as formulas 2 and 3 store them, compiled by the same Jace engine
        // the server evaluates them with. formulas 2 is multiplied by zero, so it is 0 for every hit.
        var engine = FormulaManager.Instance.CalculationEngine;

        var cancel = new Formula
        {
            Id = SkillCastInterruptRules.CastingCancelPercentFormulaId,
            TextFormula = "0 * (100 / casting_tolerance) * (1 + (1 * floor(damage_percent / 10)))"
        };
        await Assert.That(cancel.Prepare()).IsTrue();

        var delay = new Formula
        {
            Id = SkillCastInterruptRules.CastingDelayTimeFormulaId,
            TextFormula = "300 * (100 / casting_tolerance) * (0.42 * floor(damage_percent / 5))"
        };
        await Assert.That(delay.Prepare()).IsTrue();

        await Assert.That(engine).IsNotNull();

        // 25% of maximum health, casting_tolerance 100.
        var hit25 = new Dictionary<string, double> { ["damage_percent"] = 25, ["casting_tolerance"] = 100 };
        await Assert.That(SkillCastInterruptRules.CancelPercent(cancel.Evaluate(hit25))).IsEqualTo(0d);
        await Assert.That(SkillCastInterruptRules.DelayMilliseconds(delay.Evaluate(hit25))).IsEqualTo(630);

        // 15%: floor(15 / 5) = 3 -> 300 * 0.42 * 3 = 378.
        var hit15 = new Dictionary<string, double> { ["damage_percent"] = 15, ["casting_tolerance"] = 100 };
        await Assert.That(SkillCastInterruptRules.DelayMilliseconds(delay.Evaluate(hit15))).IsEqualTo(378);

        // Under 5% the delay formula floors to zero and the cast is untouched.
        var hit4 = new Dictionary<string, double> { ["damage_percent"] = 4, ["casting_tolerance"] = 100 };
        await Assert.That(SkillCastInterruptRules.DelayMilliseconds(delay.Evaluate(hit4))).IsEqualTo(0);

        // casting_tolerance 200 halves the delay: 300 * (100 / 200) * (0.42 * 5) = 315.
        var tolerant = new Dictionary<string, double> { ["damage_percent"] = 25, ["casting_tolerance"] = 200 };
        await Assert.That(SkillCastInterruptRules.DelayMilliseconds(delay.Evaluate(tolerant))).IsEqualTo(315);
    }

    [Test]
    public async Task Evaluate_WithoutALoadedRow_ReportsNoValue()
    {
        // A row the process never loaded is "absent", not an exception and not a zero-length delay.
        await Assert.That(SkillCastInterruptRules.Evaluate(25, 100, 999999)).IsNull();
    }

    /// <summary>
    /// The wiring: a hit on a unit that is casting breaks the cast when the template says a big hit does.
    /// </summary>
    [Test]
    public async Task DamageOnACastingUnit_CancelsTheCast_WhenStopCastingOnBigHitIsSet()
    {
        var victim = new Unit { ObjId = 500, Level = 60, Hp = 1000, MaxHp = 1000 };
        var skill = new Skill
        {
            Id = 10667,
            TlId = SkillTlIdManager.GetNextId(victim),
            Template = new SkillTemplate
            {
                Id = 10667,
                CastingTime = 3000,
                StopCastingOnBigHit = true,
                CastingCancelable = true
            }
        };
        victim.SkillTask = new CastTask(skill, victim, new SkillCasterUnit(victim.ObjId), victim,
            new SkillCastUnitTarget(victim.ObjId), new SkillObject());

        // 200 of 1000 maximum health is a 20% hit, past the 10% big-hit unit.
        victim.ReduceCurrentHp(new Unit { ObjId = 501 }, 200);

        await Assert.That(skill.Cancelled).IsTrue();
        await Assert.That(victim.SkillTask).IsNull();
    }

    /// <summary>
    /// The same hit against a cast that is neither cancelable nor delayable leaves it running.
    /// </summary>
    [Test]
    public async Task DamageOnACastingUnit_LeavesAnUninterruptibleCastAlone()
    {
        var victim = new Unit { ObjId = 502, Level = 60, Hp = 1000, MaxHp = 1000 };
        var skill = new Skill
        {
            Id = 10667,
            TlId = SkillTlIdManager.GetNextId(victim),
            Template = new SkillTemplate
            {
                Id = 10667,
                CastingTime = 3000,
                StopCastingOnBigHit = false,
                CastingCancelable = false,
                CastingDelayable = false
            }
        };
        var task = new CastTask(skill, victim, new SkillCasterUnit(victim.ObjId), victim,
            new SkillCastUnitTarget(victim.ObjId), new SkillObject());
        victim.SkillTask = task;

        victim.ReduceCurrentHp(new Unit { ObjId = 503 }, 900);

        await Assert.That(skill.Cancelled).IsFalse();
        await Assert.That(victim.SkillTask).IsSameReferenceAs(task);
    }

    /// <summary>A big hit ends a channel that declares <c>stop_channeling_on_big_hit</c>.</summary>
    [Test]
    public async Task DamageOnAChannellingUnit_BreaksTheChannel()
    {
        var victim = new Unit { ObjId = 504, Level = 60, Hp = 1000, MaxHp = 1000 };
        var skill = new Skill
        {
            Id = 10714,
            TlId = SkillTlIdManager.GetNextId(victim),
            Template = new SkillTemplate
            {
                Id = 10714,
                ChannelingTime = 12000,
                ChannelingTick = 1000,
                ChannelingMana = 14,
                StopChannelingOnBigHit = true
            }
        };
        victim.SkillTask = new EndChannelingTask(skill, victim, new SkillCasterUnit(victim.ObjId), victim,
            new SkillCastUnitTarget(victim.ObjId), new SkillObject(), null);

        victim.ReduceCurrentHp(new Unit { ObjId = 505 }, 300);

        await Assert.That(skill.Cancelled).IsTrue();
        await Assert.That(victim.SkillTask).IsNull();
        await Assert.That(skill.TlId).IsEqualTo((ushort)0);
    }
}
