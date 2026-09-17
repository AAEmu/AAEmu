using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// The gate World puts between two zone-driven NPC basic attacks. The live log is the evidence for the
/// shape: <c>ZWStartSkill caster=792 skill=2 … almighty=False result=CooldownTime</c> nine times in five
/// seconds, i.e. the zone asking for a swing about twice a second and World's flat 1 500 ms constant
/// refusing most of them.
/// </summary>
public class NpcSwingGateRulesTests
{
    [Test]
    public async Task SwingIntervalMs_GetAttackDelaysAnswerForABasicAttack_IsKept()
    {
        // skills id 2 (근접 공격) carries cooldown_time 300 and the helper adds its 1000 ms recovery.
        await Assert.That(NpcSwingGateRules.SwingIntervalMs(1300.0)).IsEqualTo(1300);
    }

    [Test]
    public async Task SwingIntervalMs_HastedUnit_ShortensTheGate()
    {
        // attack_speed_mul +1000 halves the interval the helper returns.
        await Assert.That(NpcSwingGateRules.SwingIntervalMs(650.0)).IsEqualTo(650);
        // …and the floor is the one the character weapon-speed branch uses.
        await Assert.That(NpcSwingGateRules.SwingIntervalMs(100.0)).IsEqualTo(400);
    }

    [Test]
    public async Task SwingIntervalMs_SlowedUnit_LengthensTheGate()
    {
        await Assert.That(NpcSwingGateRules.SwingIntervalMs(3887.0)).IsEqualTo(3887);
        await Assert.That(NpcSwingGateRules.SwingIntervalMs(9000.0)).IsEqualTo(5000);
    }

    [Test]
    [Arguments(0.0)]
    [Arguments(-1.0)]
    [Arguments(double.NaN)]
    public async Task SwingIntervalMs_NothingToGoOn_KeepsTheOldConstant(double delay)
    {
        // A zero or unset delay must not open the gate: it falls back to the constant World used before.
        await Assert.That(NpcSwingGateRules.SwingIntervalMs(delay)).IsEqualTo(1500);
    }

    [Test]
    public async Task BypassesWorldGate_OnlyWhenTheZoneAsksForIt()
    {
        // Every one of the 73 ZWStartSkill requests in the live log carried almighty=False, so this is a
        // no-op today; it is the switch the zone can use to say "my cadence, not yours".
        await Assert.That(NpcSwingGateRules.BypassesWorldGate(casterIsNpc: true, almighty: true)).IsTrue();
        await Assert.That(NpcSwingGateRules.BypassesWorldGate(casterIsNpc: true, almighty: false)).IsFalse();
    }

    [Test]
    public async Task BypassesWorldGate_PlayerCaster_IsNotThisRulesBusiness()
    {
        // The caller passes bypassGcd for a player already; this helper only ever adds the almighty case.
        await Assert.That(NpcSwingGateRules.BypassesWorldGate(casterIsNpc: false, almighty: true)).IsFalse();
    }
}
