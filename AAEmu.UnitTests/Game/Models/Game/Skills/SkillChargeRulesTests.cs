using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillChargeRulesTests
{
    private static readonly DateTime T0 = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task InitialPool_StartsWithEveryCharge()
    {
        var state = SkillChargeRules.Initial(2);
        await Assert.That(state.Max).IsEqualTo(2);
        await Assert.That(state.Current).IsEqualTo(2);
        await Assert.That(state.NextRechargeUtc).IsEqualTo(DateTime.MaxValue);
    }

    [Test]
    public async Task Consume_SpendsOneCharge_AndStartsTheClockOnTheFirstSpend()
    {
        // 11368 매의 발톱: 2 charges, 8,000 ms recharge.
        var state = SkillChargeRules.Consume(SkillChargeRules.Initial(2), 8000, T0);

        await Assert.That(state.Current).IsEqualTo(1);
        await Assert.That(state.NextRechargeUtc).IsEqualTo(T0.AddMilliseconds(8000));
        await Assert.That(SkillChargeRules.ArmsCooldownAfterCast(state)).IsFalse();
    }

    [Test]
    public async Task Consume_OnTheLastCharge_ArmsTheCooldown()
    {
        var state = SkillChargeRules.Consume(SkillChargeRules.Initial(2), 8000, T0);
        state = SkillChargeRules.Consume(state, 8000, T0.AddSeconds(1));

        await Assert.That(state.Current).IsEqualTo(0);
        await Assert.That(SkillChargeRules.ArmsCooldownAfterCast(state)).IsTrue();
    }

    [Test]
    public async Task Consume_OnAnEmptyPool_ChangesNothing()
    {
        var empty = new SkillChargeRules.ChargeState(2, 0, T0.AddMilliseconds(8000));
        var state = SkillChargeRules.Consume(empty, 8000, T0.AddSeconds(1));

        await Assert.That(state).IsEqualTo(empty);
    }

    [Test]
    public async Task Recharge_ReturnsOneChargePerElapsedInterval()
    {
        // 13281 다발 사격: 5 charges, 22,000 ms recharge; three spent leaves two.
        var spent = SkillChargeRules.Consume(SkillChargeRules.Initial(5), 22000, T0);
        spent = SkillChargeRules.Consume(spent, 22000, T0);
        spent = SkillChargeRules.Consume(spent, 22000, T0);
        await Assert.That(spent.Current).IsEqualTo(2);

        // Still inside the first interval: nothing back yet.
        var state = SkillChargeRules.Recharge(spent, 22000, T0.AddSeconds(21), out var granted);
        await Assert.That(granted).IsEqualTo(0);
        await Assert.That(state.Current).IsEqualTo(2);

        // Two intervals elapsed: two charges, not all three — the pool refills one at a time.
        state = SkillChargeRules.Recharge(spent, 22000, T0.AddSeconds(44), out granted);
        await Assert.That(granted).IsEqualTo(2);
        await Assert.That(state.Current).IsEqualTo(4);

        // The third interval completes the pool.
        state = SkillChargeRules.Recharge(state, 22000, T0.AddSeconds(66), out granted);
        await Assert.That(granted).IsEqualTo(1);
        await Assert.That(state.Current).IsEqualTo(5);
    }

    [Test]
    public async Task Recharge_StopsAtThePoolCeiling()
    {
        var spent = SkillChargeRules.Consume(SkillChargeRules.Initial(3), 9000, T0);
        var state = SkillChargeRules.Recharge(spent, 9000, T0.AddDays(1), out var granted);

        await Assert.That(state.Current).IsEqualTo(3);
        await Assert.That(granted).IsEqualTo(1);
        await Assert.That(state.NextRechargeUtc).IsEqualTo(DateTime.MaxValue);
    }

    [Test]
    public async Task Recharge_NeverRefills_WhenTheSkillDeclaresNoInterval()
    {
        var spent = SkillChargeRules.Consume(SkillChargeRules.Initial(2), 0, T0);
        var state = SkillChargeRules.Recharge(spent, 0, T0.AddDays(1), out var granted);

        await Assert.That(granted).IsEqualTo(0);
        await Assert.That(state.Current).IsEqualTo(1);
    }

    [Test]
    public async Task ChangeCount_ClampsIntoThePool()
    {
        // Special effect 166: 56119 adds three to a 3-charge skill, 35202 adds one to 38893.
        var spent = SkillChargeRules.Consume(SkillChargeRules.Consume(SkillChargeRules.Initial(3), 86400000, T0),
            86400000, T0);
        await Assert.That(spent.Current).IsEqualTo(1);

        var refilled = SkillChargeRules.ChangeCount(spent, 3, 3);
        await Assert.That(refilled.Current).IsEqualTo(3);
        // A refilled pool has no recharge clock to run.
        await Assert.That(refilled.NextRechargeUtc).IsEqualTo(DateTime.MaxValue);

        var clamped = SkillChargeRules.ChangeCount(SkillChargeRules.Initial(3), 3, 10);
        await Assert.That(clamped.Current).IsEqualTo(3);

        var drained = SkillChargeRules.ChangeCount(SkillChargeRules.Initial(3), 3, -10);
        await Assert.That(drained.Current).IsEqualTo(0);
    }

    [Test]
    public async Task ChangeRechargeTime_ShiftsTheRunningTimer()
    {
        // Special effect 167: -3000 ms off 38893 빛의 사격's 16,000 ms recharge.
        var spent = SkillChargeRules.Consume(SkillChargeRules.Initial(3), 16000, T0);
        var early = SkillChargeRules.ChangeRechargeTime(spent, -3000, T0.AddSeconds(1));

        await Assert.That(early.NextRechargeUtc).IsEqualTo(T0.AddMilliseconds(13000));

        // An offset that would land in the past leaves the charge due now.
        var due = SkillChargeRules.ChangeRechargeTime(spent, -30000, T0.AddSeconds(1));
        await Assert.That(due.NextRechargeUtc).IsEqualTo(T0.AddSeconds(1));

        var state = SkillChargeRules.Recharge(due, 16000, T0.AddSeconds(2), out var granted);
        await Assert.That(granted).IsEqualTo(1);
        await Assert.That(state.Current).IsEqualTo(3);
    }

    [Test]
    public async Task RestartRecharge_SetsTheStatedInterval_OnlyWhileAChargeIsMissing()
    {
        var spent = SkillChargeRules.Consume(SkillChargeRules.Initial(3), 16000, T0);
        var restarted = SkillChargeRules.RestartRecharge(spent, 20000, T0.AddSeconds(5));

        await Assert.That(restarted.NextRechargeUtc).IsEqualTo(T0.AddSeconds(25));

        // A full pool has no timer to restart.
        var full = SkillChargeRules.RestartRecharge(SkillChargeRules.Initial(3), 20000, T0);
        await Assert.That(full.NextRechargeUtc).IsEqualTo(DateTime.MaxValue);
    }

    [Test]
    public async Task UnitCooldowns_TwoChargeSkill_CastsTwiceThenWaits()
    {
        // End to end through the store the cast path uses: 11368 매의 발톱, 2 charges.
        var cooldowns = new UnitCooldowns();

        await Assert.That(cooldowns.GetCharges(11368u, 2, 8000)).IsEqualTo(2);
        await Assert.That(cooldowns.ConsumeCharge(11368u, 2, 8000)).IsEqualTo(1);
        await Assert.That(cooldowns.ConsumeCharge(11368u, 2, 8000)).IsEqualTo(0);
        // The third cast finds no charge; nothing is spent and the caller arms the cooldown instead.
        await Assert.That(cooldowns.ConsumeCharge(11368u, 2, 8000)).IsEqualTo(0);
    }

    [Test]
    public async Task UnitCooldowns_ChangeChargeCount_AndRechargeTime_ActOnThePool()
    {
        var cooldowns = new UnitCooldowns();
        cooldowns.ConsumeCharge(38893u, 3, 16000);
        await Assert.That(cooldowns.GetCharges(38893u, 3, 16000)).IsEqualTo(2);

        cooldowns.ChangeChargeCount(38893u, 3, 1);
        await Assert.That(cooldowns.GetCharges(38893u, 3, 16000)).IsEqualTo(3);

        // A refilled pool ignores a recharge-time offset instead of throwing.
        cooldowns.ChangeChargeRechargeTime(38893u, 3, -3000);
        await Assert.That(cooldowns.GetCharges(38893u, 3, 16000)).IsEqualTo(3);
    }

    [Test]
    public async Task UnitCooldowns_ZeroChargeSkill_KeepsAnEmptyPool()
    {
        var cooldowns = new UnitCooldowns();
        await Assert.That(cooldowns.GetCharges(10025u, 0, 0)).IsEqualTo(0);
        await Assert.That(cooldowns.ConsumeCharge(10025u, 0, 0)).IsEqualTo(0);
    }
}
