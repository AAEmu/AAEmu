using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class BuffStackRulesTests
{
    /// <summary>The sail-trim family ceiling: one instance per sail, sixty applications each.</summary>
    private const int SailTrimMaxStack = 60;

    [Test]
    public async Task CanGrow_AcceptsApplicationsBelowTheCeiling()
    {
        await Assert.That(BuffStackRules.CanGrow(1, SailTrimMaxStack)).IsTrue();
        await Assert.That(BuffStackRules.CanGrow(59, SailTrimMaxStack)).IsTrue();
    }

    [Test]
    public async Task CanGrow_StopsAtTheCeiling()
    {
        await Assert.That(BuffStackRules.CanGrow(SailTrimMaxStack, SailTrimMaxStack)).IsFalse();
    }

    [Test]
    public async Task CanGrow_StopsAboveTheCeiling()
    {
        // A member restored from a save could exceed a ceiling that has since been lowered.
        await Assert.That(BuffStackRules.CanGrow(SailTrimMaxStack + 1, SailTrimMaxStack)).IsFalse();
    }

    [Test]
    public async Task CanGrow_RefusesFamiliesThatDoNotStack()
    {
        // Ceilings of zero and one both mean "one application"; growing either would let a single-stack
        // buff double its modifiers instead of simply refreshing.
        await Assert.That(BuffStackRules.CanGrow(1, 0)).IsFalse();
        await Assert.That(BuffStackRules.CanGrow(1, 1)).IsFalse();
    }

    [Test]
    public async Task ShouldTransform_FiresAtTheCeilingWhenATransformIsNamed()
    {
        // Tension 5793 → line-broken 5794 at 20. Sail trim has no transform and must stay put.
        await Assert.That(BuffStackRules.ShouldTransform(20, 20, 5794)).IsTrue();
        await Assert.That(BuffStackRules.ShouldTransform(19, 20, 5794)).IsFalse();
        await Assert.That(BuffStackRules.ShouldTransform(20, 20, 0)).IsFalse();
        await Assert.That(BuffStackRules.ShouldTransform(1, 1, 5794)).IsFalse();
    }

    [Test]
    public async Task ScaledModifier_GrowsSailTrimBySixPerStack()
    {
        // Sail trim is +6 move_speed_mul per application, ceiling 60. The first tick is +6;
        // sixty ticks are +360. A flat +36% on the first application is the ceiling, not the start.
        await Assert.That(BuffStackRules.ScaledModifier(6, 0, 1, 1)).IsEqualTo(6);
        await Assert.That(BuffStackRules.ScaledModifier(6, 0, 1, 2)).IsEqualTo(12);
        await Assert.That(BuffStackRules.ScaledModifier(6, 0, 1, 60)).IsEqualTo(360);
        await Assert.That(BuffStackRules.ScaledModifier(6, 0, 1, 0)).IsEqualTo(6);
    }

    [Test]
    public async Task Refresh_KeepsAPermanentInstance()
    {
        // Fishing 4053 is duration 0 / Refresh. A second apply must not overwrite.
        await Assert.That(BuffStackRules.ShouldOverwriteOnRefresh(0, 0)).IsFalse();
        await Assert.That(BuffStackRules.ShouldOverwriteOnRefresh(1500, 0)).IsTrue();
        await Assert.That(BuffStackRules.ShouldOverwriteOnRefresh(0, 1500)).IsTrue();
        await Assert.That(BuffStackRules.ShouldOverwriteOnRefresh(1500, 800)).IsTrue();
    }

    [Test]
    public async Task DispelTask_OnlyForTimedOrTicking()
    {
        await Assert.That(BuffStackRules.ShouldScheduleDispel(0, 0)).IsFalse();
        await Assert.That(BuffStackRules.ShouldScheduleDispel(1500, 0)).IsTrue();
        await Assert.That(BuffStackRules.ShouldScheduleDispel(0, 800)).IsTrue();
    }

    [Test]
    public async Task Enum_MatchesTheShippedEnumBuffStackRuleIds()
    {
        // enum_buff_stack_rule, seven rows: 1 refresh, 2 charge_refresh, 3 charge_extend, 4 multiple,
        // 5 extend, 6 independent, 7 multiple_decrease_one. SkillManager casts `stack_rule_id` straight
        // to this enum, so a member that drifts off its id silently sends that family to another rule's
        // branch. Rule 7 was missing entirely and its 28 rows landed in the default branch.
        await Assert.That((int)BuffStackRule.Refresh).IsEqualTo(1);
        await Assert.That((int)BuffStackRule.ChargeRefresh).IsEqualTo(2);
        await Assert.That((int)BuffStackRule.ChargeExtend).IsEqualTo(3);
        await Assert.That((int)BuffStackRule.Multiple).IsEqualTo(4);
        await Assert.That((int)BuffStackRule.Extend).IsEqualTo(5);
        await Assert.That((int)BuffStackRule.Independent).IsEqualTo(6);
        await Assert.That((int)BuffStackRule.MultipleDecreaseOne).IsEqualTo(7);
    }

    [Test]
    public async Task IsCasterScoped_OnlyForTheRulesThatHoldAnInstancePerCaster()
    {
        // 4 multiple (729 rows), 6 independent (9,942) and 7 multiple_decrease_one (28) are the rules a
        // second caster has to be able to sit beside. Refresh, ChargeRefresh, Extend and ChargeExtend are
        // one instance for the whole family: a second caster continues the same effect.
        await Assert.That(BuffStackRules.IsCasterScoped(BuffStackRule.Independent)).IsTrue();
        await Assert.That(BuffStackRules.IsCasterScoped(BuffStackRule.Multiple)).IsTrue();
        await Assert.That(BuffStackRules.IsCasterScoped(BuffStackRule.MultipleDecreaseOne)).IsTrue();
        await Assert.That(BuffStackRules.IsCasterScoped(BuffStackRule.Refresh)).IsFalse();
        await Assert.That(BuffStackRules.IsCasterScoped(BuffStackRule.ChargeRefresh)).IsFalse();
        await Assert.That(BuffStackRules.IsCasterScoped(BuffStackRule.Extend)).IsFalse();
        await Assert.That(BuffStackRules.IsCasterScoped(BuffStackRule.ChargeExtend)).IsFalse();
    }

    [Test]
    public async Task AccumulatesApplications_OnlyForMultiple()
    {
        // Rule 4 is the counting rule. Its max_stack runs to 10,000 (25024 따뜻한 히라마 스튜 재료),
        // 9,999 (24702 향연수호전 자원사용함), 600 (24701 저승 공헌도) and 60 (20860 해풍 응용, the
        // permanent sail-wind family), which is a count on one icon and not that many live instances.
        await Assert.That(BuffStackRules.AccumulatesApplications(BuffStackRule.Multiple)).IsTrue();
        await Assert.That(BuffStackRules.AccumulatesApplications(BuffStackRule.Independent)).IsFalse();
        await Assert.That(BuffStackRules.AccumulatesApplications(BuffStackRule.MultipleDecreaseOne)).IsFalse();
    }

    [Test]
    public async Task IsInstancePerApplication_OnlyForMultipleDecreaseOne()
    {
        // Rule 7's name is its semantics: several instances, so they fall off one at a time. Rule 4's
        // applications share one timer and end together.
        await Assert.That(BuffStackRules.IsInstancePerApplication(BuffStackRule.MultipleDecreaseOne)).IsTrue();
        await Assert.That(BuffStackRules.IsInstancePerApplication(BuffStackRule.Multiple)).IsFalse();
        await Assert.That(BuffStackRules.IsInstancePerApplication(BuffStackRule.Independent)).IsFalse();
    }

    [Test]
    public async Task InstanceCeiling_TreatsAMissingCeilingAsOne()
    {
        // Every buff of rules 3-7 authors a positive max_stack, but a zero read has to mean "one
        // instance" rather than "no instance", or the application would be dropped instead of landing.
        await Assert.That(BuffStackRules.InstanceCeiling(0)).IsEqualTo(1);
        await Assert.That(BuffStackRules.InstanceCeiling(30)).IsEqualTo(30);
    }

    [Test]
    public async Task CanAddInstance_StopsAtTheCeiling()
    {
        // 25442 이동 속도 증가 is rule 7 with max_stack 30; 24879 표적 has 2. A missing ceiling reads as
        // one instance: the application lands, the second one replaces it.
        await Assert.That(BuffStackRules.CanAddInstance(0, 30)).IsTrue();
        await Assert.That(BuffStackRules.CanAddInstance(29, 30)).IsTrue();
        await Assert.That(BuffStackRules.CanAddInstance(30, 30)).IsFalse();
        await Assert.That(BuffStackRules.CanAddInstance(2, 2)).IsFalse();
        await Assert.That(BuffStackRules.CanAddInstance(0, 0)).IsTrue();
        await Assert.That(BuffStackRules.CanAddInstance(1, 0)).IsFalse();
    }

    [Test]
    public async Task ExtendedDuration_AddsWhatIsLeftToTheIncomingApplication()
    {
        // Extend adds: 2 s left of a 5 s instance plus an incoming 5 s application is 7 s, where refresh
        // would leave 5 s (BuffStackAddBuffTests holds both rules to that at the level they land).
        await Assert.That(BuffStackRules.ExtendedDuration(5000, 2000)).IsEqualTo(7000);
        await Assert.That(BuffStackRules.ExtendedDuration(5000, 0)).IsEqualTo(5000);
    }

    [Test]
    public async Task ExtendedDuration_FloorsThePermanentSentinelAtZero()
    {
        // GetTimeLeft() answers -1 for a duration-0 instance. Adding that sentinel shaves a millisecond
        // off the incoming application (1500 became 1499); a permanent instance contributes nothing.
        await Assert.That(BuffStackRules.ExtendedDuration(1500, -1)).IsEqualTo(1500);
        await Assert.That(BuffStackRules.ExtendedDuration(0, -1)).IsEqualTo(0);
    }

    [Test]
    public async Task SummedCharge_AddsAndHoldsAtTheCeiling()
    {
        // 22574 보호막 is charge_extend with max_charge 20,000; 899 누적 피해 has 1,000.
        await Assert.That(BuffStackRules.SummedCharge(300, 300, 1000)).IsEqualTo(600);
        await Assert.That(BuffStackRules.SummedCharge(900, 300, 1000)).IsEqualTo(1000);
        await Assert.That(BuffStackRules.SummedCharge(1000, 300, 1000)).IsEqualTo(1000);
    }

    [Test]
    public async Task SummedCharge_LeavesFamiliesWithNoAuthoredCeilingUnbounded()
    {
        // Four charge_extend rows ship max_charge 0 (35 차원의 틈, 767 충전테스트, 23473 무모함, 26828
        // 느려짐). Zero there is "no ceiling authored", so clamping would discard the incoming charge.
        await Assert.That(BuffStackRules.SummedCharge(5, 7, 0)).IsEqualTo(12);
        await Assert.That(BuffStackRules.SummedCharge(0, 0, 0)).IsEqualTo(0);
    }

    [Test]
    public async Task SummedCharge_IgnoresNegativeInputs()
    {
        // A drained charge is floored at zero by ConsumeCharge, and the initial roll is non-negative;
        // neither may pull the total below what the live instance already holds.
        await Assert.That(BuffStackRules.SummedCharge(-4, 6, 0)).IsEqualTo(6);
        await Assert.That(BuffStackRules.SummedCharge(6, -4, 0)).IsEqualTo(6);
    }

    [Test]
    public async Task WireStack_ReportsAnInstanceOwnApplicationsForPerCasterRules()
    {
        // Two casters of an Independent buff hold one application each. The family total is 2 on both,
        // and reporting it would print "x2" on each icon — the identical-icons defect the remarks
        // describe — so the instance's own count wins.
        await Assert.That(BuffStackRules.WireStack(BuffStackRule.Independent, 1, 2)).IsEqualTo(1u);
        await Assert.That(BuffStackRules.WireStack(BuffStackRule.Multiple, 3, 5)).IsEqualTo(3u);
        await Assert.That(BuffStackRules.WireStack(BuffStackRule.MultipleDecreaseOne, 1, 4)).IsEqualTo(1u);
    }

    [Test]
    public async Task WireStack_ReportsTheFamilyTotalForTheOneInstanceRules()
    {
        // Refresh and ChargeRefresh live as one instance per family, so the family total is the count the
        // icon carries — sail trim at sixty stacks among them.
        await Assert.That(BuffStackRules.WireStack(BuffStackRule.Refresh, 1, 60)).IsEqualTo(60u);
        await Assert.That(BuffStackRules.WireStack(BuffStackRule.ChargeRefresh, 1, 3)).IsEqualTo(3u);
        await Assert.That(BuffStackRules.WireStack(BuffStackRule.Extend, 1, 4)).IsEqualTo(4u);
    }

    [Test]
    public async Task WireStack_NeverReportsZeroApplications()
    {
        await Assert.That(BuffStackRules.WireStack(BuffStackRule.Refresh, 0, 0)).IsEqualTo(1u);
        await Assert.That(BuffStackRules.WireStack(BuffStackRule.Independent, 0, 0)).IsEqualTo(1u);
    }
}
