using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

/// <summary>
/// The per-skill charge pool behind <c>skills.charge_count</c>. The intervals used here are the shipped
/// ones: 38893 빛의 사격 is 3 charges at <c>charge_cooldown_time</c> 16000, 11368 매의 발톱 is 2 at 8000,
/// 44677 영구동토 is 3 at 86400000.
/// </summary>
public class UnitChargesTests
{
    private const uint LightShot = 38893;
    private const uint Talons = 11368;
    private const int LightShotCharges = 3;
    private const uint LightShotRechargeMs = 16000;

    private static readonly DateTime Start = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ANewPool_StartsFull()
    {
        var charges = new UnitCharges();

        var snapshot = charges.GetSnapshot(LightShot, LightShotCharges, LightShotRechargeMs, Start);

        await Assert.That(snapshot.Available).IsEqualTo(3);
        await Assert.That(snapshot.Max).IsEqualTo(3);
        await Assert.That(snapshot.RechargeMs).IsEqualTo(LightShotRechargeMs);
    }

    [Test]
    public async Task Spending_SpendsOneChargePerCastUntilThePoolIsEmpty()
    {
        var charges = new UnitCharges();

        await Assert.That(charges.TrySpend(LightShot, LightShotCharges, LightShotRechargeMs, Start)).IsTrue();
        await Assert.That(charges.TrySpend(LightShot, LightShotCharges, LightShotRechargeMs, Start)).IsTrue();
        await Assert.That(charges.TrySpend(LightShot, LightShotCharges, LightShotRechargeMs, Start)).IsTrue();

        // The fourth cast finds nothing left, which is what makes Skill.ApplyCooldownOrCharge arm the
        // skill's own cooldown instead.
        await Assert.That(charges.TrySpend(LightShot, LightShotCharges, LightShotRechargeMs, Start)).IsFalse();
        var snapshot = charges.GetSnapshot(LightShot, LightShotCharges, LightShotRechargeMs, Start);
        await Assert.That(snapshot.Available).IsEqualTo(0);
    }

    [Test]
    public async Task AnIntervalLater_OneChargeIsBack()
    {
        var charges = new UnitCharges();
        for (var i = 0; i < LightShotCharges; i++)
            charges.TrySpend(LightShot, LightShotCharges, LightShotRechargeMs, Start);

        var snapshot = charges.GetSnapshot(LightShot, LightShotCharges, LightShotRechargeMs,
            Start.AddSeconds(16));

        await Assert.That(snapshot.Available).IsEqualTo(1);
    }

    [Test]
    public async Task TheUnusedRemainder_KeepsRunningTowardTheNextCharge()
    {
        var charges = new UnitCharges();
        for (var i = 0; i < LightShotCharges; i++)
            charges.TrySpend(LightShot, LightShotCharges, LightShotRechargeMs, Start);

        // One second past the first interval: 1 charge banked, 1 second already spent on the next one.
        var first = charges.GetSnapshot(LightShot, LightShotCharges, LightShotRechargeMs, Start.AddSeconds(17));
        await Assert.That(first.Available).IsEqualTo(1);

        // 15 s later the second interval completes. A pool that restarted its clock at the last credit
        // would still be one second short and report 1.
        var second = charges.GetSnapshot(LightShot, LightShotCharges, LightShotRechargeMs, Start.AddSeconds(32));
        await Assert.That(second.Available).IsEqualTo(2);
    }

    [Test]
    public async Task AFullPool_DoesNotBankProgress()
    {
        var charges = new UnitCharges();
        charges.GetSnapshot(Talons, 2, 8000, Start);

        var snapshot = charges.GetSnapshot(Talons, 2, 8000, Start.AddHours(5));

        await Assert.That(snapshot.Available).IsEqualTo(2);
    }

    [Test]
    public async Task ChangeChargeSkillCount_HandsOutTheChargeItAdds()
    {
        var charges = new UnitCharges();
        charges.GetSnapshot(LightShot, LightShotCharges, LightShotRechargeMs, Start);

        // 35202: +1 on 38893, the one type-166 row a skill reaches.
        var snapshot = charges.AddMax(LightShot, 1, LightShotCharges, LightShotRechargeMs, Start);

        await Assert.That(snapshot.Max).IsEqualTo(4);
        await Assert.That(snapshot.Available).IsEqualTo(4);
    }

    [Test]
    public async Task ChangeChargeSkillCount_LoweringTheCeiling_DropsWhatNoLongerFits()
    {
        var charges = new UnitCharges();
        charges.GetSnapshot(LightShot, LightShotCharges, LightShotRechargeMs, Start);

        var snapshot = charges.AddMax(LightShot, -2, LightShotCharges, LightShotRechargeMs, Start);

        await Assert.That(snapshot.Max).IsEqualTo(1);
        await Assert.That(snapshot.Available).IsEqualTo(1);
    }

    [Test]
    public async Task ChangeChargeSkillCount_AtZero_LeavesNoPool()
    {
        var charges = new UnitCharges();
        charges.GetSnapshot(Talons, 2, 8000, Start);
        charges.TrySpend(Talons, 2, 8000, Start);

        var snapshot = charges.AddMax(Talons, -5, 2, 8000, Start);

        await Assert.That(snapshot.Max).IsEqualTo(0);
        await Assert.That(snapshot.Available).IsEqualTo(0);
        await Assert.That(charges.TrySpend(Talons, 2, 8000, Start)).IsFalse();
    }

    [Test]
    public async Task ChangeChargeCooldown_ShiftsTheInterval()
    {
        var charges = new UnitCharges();
        charges.GetSnapshot(LightShot, LightShotCharges, LightShotRechargeMs, Start);

        // 35203: -3000 off 38893's 16000 ms.
        var snapshot = charges.AddRecharge(LightShot, -3000, LightShotCharges, LightShotRechargeMs, Start);

        await Assert.That(snapshot.RechargeMs).IsEqualTo(13000u);
        // And the shift sticks: a later credit uses the new interval, not the template's.
        var after = charges.GetSnapshot(LightShot, LightShotCharges, LightShotRechargeMs, Start.AddSeconds(13));
        await Assert.That(after.RechargeMs).IsEqualTo(13000u);
    }

    [Test]
    public async Task ChargeCooldown_SetsTheIntervalOutright()
    {
        var charges = new UnitCharges();
        charges.GetSnapshot(LightShot, LightShotCharges, LightShotRechargeMs, Start);

        var snapshot = charges.SetRecharge(LightShot, 22000, LightShotCharges, LightShotRechargeMs, Start);

        await Assert.That(snapshot.RechargeMs).IsEqualTo(22000u);
        // 21 s is past the template's 16 s and short of the effect's 22 s, so nothing comes back yet.
        var early = charges.GetSnapshot(LightShot, LightShotCharges, LightShotRechargeMs, Start.AddSeconds(21));
        await Assert.That(early.Available).IsEqualTo(3);
    }

    [Test]
    public async Task Snapshots_CreditEveryPoolTheyReport()
    {
        var charges = new UnitCharges();
        for (var i = 0; i < LightShotCharges; i++)
            charges.TrySpend(LightShot, LightShotCharges, LightShotRechargeMs, Start);
        charges.TrySpend(Talons, 2, 8000, Start);
        charges.TrySpend(Talons, 2, 8000, Start);

        // Nine seconds is one of 매의 발톱's 8000 ms intervals and only half of 빛의 사격's 16000 ms, so
        // the two pools have to answer differently.
        var snapshots = charges.GetSnapshots(10, Start.AddSeconds(9));

        await Assert.That(snapshots.Count).IsEqualTo(2);
        await Assert.That(snapshots[0].SkillId).IsEqualTo(Talons);
        await Assert.That(snapshots[0].Available).IsEqualTo(1);
        await Assert.That(snapshots[1].SkillId).IsEqualTo(LightShot);
        await Assert.That(snapshots[1].Available).IsEqualTo(0);
    }

    [Test]
    public async Task Snapshots_AreCappedAndOrderedBySkillId()
    {
        var charges = new UnitCharges();
        charges.GetSnapshot(Talons, 2, 8000, Start);
        charges.GetSnapshot(LightShot, LightShotCharges, LightShotRechargeMs, Start);

        var snapshots = charges.GetSnapshots(1, Start);

        await Assert.That(snapshots.Count).IsEqualTo(1);
        await Assert.That(snapshots[0].SkillId).IsEqualTo(Talons);
    }

    [Test]
    public async Task Snapshots_RejectANegativeCeiling()
    {
        var charges = new UnitCharges();

        await Assert.That(() => charges.GetSnapshots(-1, Start)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ASkillWithoutCharges_KeepsNoPoolTheCasterCanSpend()
    {
        // The 12 charge_cooldown rows (type 158) are dormant and the skills they would touch author
        // charge_count 0, so a pool for such a skill has nothing to spend even once one exists.
        var charges = new UnitCharges();
        charges.SetRecharge(LightShot, 20000, templateMax: 0, templateRechargeMs: 0, Start);

        await Assert.That(charges.TrySpend(LightShot, 0, 0, Start)).IsFalse();
    }
}
