using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class ChargeSkillRulesTests
{
    [Test]
    public async Task NoTimePassed_CreditsNothing()
    {
        var refill = ChargeSkillRules.Refill(available: 1, max: 3, rechargeMs: 16000, TimeSpan.Zero);

        await Assert.That(refill.Available).IsEqualTo(1);
        await Assert.That(refill.Since).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task OneInterval_CreditsExactlyOneCharge()
    {
        // 38893 빛의 사격: charge_count 3, charge_cooldown_time 16000 (cooldown_time is 9000 and is the
        // lockout the empty pool arms, not the recharge).
        var refill = ChargeSkillRules.Refill(available: 0, max: 3, rechargeMs: 16000, TimeSpan.FromSeconds(16));

        await Assert.That(refill.Available).IsEqualTo(1);
        await Assert.That(refill.Since).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task TheRemainder_KeepsRunningTowardTheNextCharge()
    {
        var refill = ChargeSkillRules.Refill(available: 0, max: 3, rechargeMs: 16000, TimeSpan.FromSeconds(17));

        await Assert.That(refill.Available).IsEqualTo(1);
        await Assert.That(refill.Since).IsEqualTo(TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task SeveralIntervals_CreditOneChargeEach_UpToTheCeiling()
    {
        var refill = ChargeSkillRules.Refill(available: 0, max: 3, rechargeMs: 16000, TimeSpan.FromSeconds(70));

        await Assert.That(refill.Available).IsEqualTo(3);
        await Assert.That(refill.Since).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task ADayLongInterval_CreditsNothingEarly()
    {
        // 44677 영구동토 / 44702 피의 복수 / 44713 시간의 고치 / 44727 용수바람: 3 charges at
        // charge_cooldown_time 86400000 over a 300000 ms cooldown.
        var refill = ChargeSkillRules.Refill(0, 3, 86400000, TimeSpan.FromHours(23));

        await Assert.That(refill.Available).IsEqualTo(0);
        await Assert.That(refill.Since).IsEqualTo(TimeSpan.FromHours(23));
    }

    [Test]
    public async Task AFullPool_BanksNothing()
    {
        // A pool at its ceiling must not store progress: firing immediately after a long idle would
        // otherwise hand out a free charge.
        var refill = ChargeSkillRules.Refill(available: 3, max: 3, rechargeMs: 16000, TimeSpan.FromHours(5));

        await Assert.That(refill.Available).IsEqualTo(3);
        await Assert.That(refill.Since).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task AZeroInterval_RefillsTheWholePool()
    {
        // No shipped row authors charge_cooldown_time 0, but the interval is writable at runtime: the
        // charge_cooldown effect (158) sets it outright and change_charge_cooldown (167) clamps its delta
        // at zero (35203 passes -3000 on 38893's 16000). Dividing by that zero must not throw, and a pool
        // that refills instantly is the only reading of "no interval" that does not deadlock the skill.
        var refill = ChargeSkillRules.Refill(0, 5, 0, TimeSpan.Zero);

        await Assert.That(refill.Available).IsEqualTo(5);
        await Assert.That(refill.Since).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task NoCeiling_MeansNoPool()
    {
        var refill = ChargeSkillRules.Refill(2, 0, 16000, TimeSpan.FromHours(1));

        await Assert.That(refill.Available).IsEqualTo(0);
    }

    [Test]
    public async Task NegativeElapsedTime_IsTreatedAsNone()
    {
        var refill = ChargeSkillRules.Refill(0, 3, 16000, TimeSpan.FromSeconds(-5));

        await Assert.That(refill.Available).IsEqualTo(0);
        await Assert.That(refill.Since).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task ChangeChargeSkillCount_MovesTheCeiling()
    {
        // 56119-56132 ask for +3 on skills that author charge_count 3; 35202 asks for +1 on 38893.
        await Assert.That(ChargeSkillRules.AddedMax(3, 3)).IsEqualTo(6);
        await Assert.That(ChargeSkillRules.AddedMax(3, 1)).IsEqualTo(4);
        await Assert.That(ChargeSkillRules.AddedMax(3, -1)).IsEqualTo(2);
    }

    [Test]
    public async Task ChangeChargeSkillCount_NeverGoesBelowZero()
    {
        await Assert.That(ChargeSkillRules.AddedMax(1, -5)).IsEqualTo(0);
    }

    [Test]
    public async Task ChangeChargeCooldown_ShiftsTheInterval()
    {
        // 35203 passes skill 38893 with -3000, off its 16000 ms charge interval.
        await Assert.That(ChargeSkillRules.ChangedRecharge(16000, -3000)).IsEqualTo(13000u);
        await Assert.That(ChargeSkillRules.ChangedRecharge(16000, 2000)).IsEqualTo(18000u);
        await Assert.That(ChargeSkillRules.ChangedRecharge(16000, 0)).IsEqualTo(16000u);
    }

    [Test]
    public async Task ChangeChargeCooldown_ClampsAtZero()
    {
        await Assert.That(ChargeSkillRules.ChangedRecharge(1000, -5000)).IsEqualTo(0u);
    }
}
