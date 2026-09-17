using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// <c>buffs.aura_radius</c> / <c>aura_slave_buff_id</c> and the rest of the aura family, as the decisions
/// the pulse makes about one candidate unit.
/// </summary>
public class AuraRulesTests
{
    // The shipped columns: 549 rows author a radius, 511 of them name a slave.
    private const int Radius20 = 20;        // 789 성전 선포
    private const uint Slave1068 = 1068;    // what 789 hands out

    [Test]
    public async Task IsAura_WithoutBothColumns_IsFalse()
    {
        await Assert.That(AuraRules.IsAura(Radius20, Slave1068)).IsTrue();

        // The 51 rows with a radius and no slave have nothing to apply; the 13 with a slave and no radius
        // have nowhere to apply it.
        await Assert.That(AuraRules.IsAura(Radius20, 0)).IsFalse();
        await Assert.That(AuraRules.IsAura(0, Slave1068)).IsFalse();
        await Assert.That(AuraRules.IsAura(0, 0)).IsFalse();
        await Assert.That(AuraRules.IsAura(-1, Slave1068)).IsFalse();
    }

    [Test]
    public async Task InRadius_IsInclusiveAtTheEdge_AndFalseWithoutARadius()
    {
        await Assert.That(AuraRules.InRadius(Radius20, 0f)).IsTrue();
        await Assert.That(AuraRules.InRadius(Radius20, 19.99f)).IsTrue();
        await Assert.That(AuraRules.InRadius(Radius20, 20f)).IsTrue();
        await Assert.That(AuraRules.InRadius(Radius20, 20.01f)).IsFalse();
        await Assert.That(AuraRules.InRadius(0, 0f)).IsFalse();
    }

    [Test]
    public async Task HasRoom_ZeroMaxCountIsUnlimited()
    {
        // aura_max_count is 0 for 30,572 rows, which is the authored "no ceiling".
        await Assert.That(AuraRules.HasRoom(0, 0)).IsTrue();
        await Assert.That(AuraRules.HasRoom(0, 10_000)).IsTrue();

        // 50 (18 rows) and 10 (17 rows) are the common authored ceilings.
        await Assert.That(AuraRules.HasRoom(10, 0)).IsTrue();
        await Assert.That(AuraRules.HasRoom(10, 9)).IsTrue();
        await Assert.That(AuraRules.HasRoom(10, 10)).IsFalse();
        await Assert.That(AuraRules.HasRoom(10, 11)).IsFalse();

        // The tightest shipped row, buff 2405 은신 이동's radius of 1 m aside, is aura_max_count 1 (14 rows).
        await Assert.That(AuraRules.HasRoom(1, 0)).IsTrue();
        await Assert.That(AuraRules.HasRoom(1, 1)).IsFalse();
    }

    [Test]
    public async Task AllowsRecipient_WithNoFlagAndAMatchingRelation_IsTrue()
    {
        await Assert.That(AuraRules.AllowsRecipient(false, false, false, false, true)).IsTrue();
        await Assert.That(AuraRules.AllowsRecipient(false, false, false, false, false)).IsFalse();
    }

    [Test]
    public async Task AllowsRecipient_CreatorOnly_AdmitsOnlyTheCaster()
    {
        // aura_creator_only is set on 86 rows: the aura is the caster's own effect.
        await Assert.That(AuraRules.AllowsRecipient(true, false, true, true, true)).IsTrue();
        await Assert.That(AuraRules.AllowsRecipient(true, false, false, true, true)).IsFalse();
        await Assert.That(AuraRules.AllowsRecipient(true, false, false, false, true)).IsFalse();
    }

    [Test]
    public async Task AllowsRecipient_ChildOnly_AdmitsTheCasterAndWhatItOwns()
    {
        // aura_child_only is set on 68 rows. 2405 은신 이동 (radius 1, slave 2408, relation any) is the
        // shape: "자신과 탑승자를 은신 상태로 만듭니다" — the caster itself and its rider.
        await Assert.That(AuraRules.AllowsRecipient(false, true, true, false, true)).IsTrue();
        await Assert.That(AuraRules.AllowsRecipient(false, true, false, true, true)).IsTrue();
        await Assert.That(AuraRules.AllowsRecipient(false, true, false, false, true)).IsFalse();
    }

    [Test]
    public async Task AllowsRecipient_TheTwoFlagsCompoundWithTheRelation()
    {
        await Assert.That(AuraRules.AllowsRecipient(true, true, true, true, true)).IsTrue();
        await Assert.That(AuraRules.AllowsRecipient(true, true, true, true, false)).IsFalse();
        await Assert.That(AuraRules.AllowsRecipient(true, true, false, true, true)).IsFalse();
        await Assert.That(AuraRules.AllowsRecipient(true, true, true, false, true)).IsTrue();
    }

    [Test]
    public async Task PulseIntervalMs_UsesTheAuthoredTick_AndDefaultsToOneSecond()
    {
        // 463 of the 511 auras author tick 0; the ones that do author one use 500..20000 ms.
        await Assert.That(AuraRules.PulseIntervalMs(0)).IsEqualTo(AuraRules.DefaultPulseIntervalMs);
        await Assert.That(AuraRules.PulseIntervalMs(-1)).IsEqualTo(AuraRules.DefaultPulseIntervalMs);
        await Assert.That(AuraRules.PulseIntervalMs(500)).IsEqualTo(500);
        await Assert.That(AuraRules.PulseIntervalMs(1000)).IsEqualTo(1000);
        await Assert.That(AuraRules.PulseIntervalMs(20000)).IsEqualTo(20000);
    }
}
