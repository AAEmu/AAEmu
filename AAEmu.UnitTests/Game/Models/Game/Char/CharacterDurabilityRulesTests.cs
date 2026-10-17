using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units.Static;

namespace AAEmu.UnitTests.Game.Models.Game.Char;

public class CharacterDurabilityRulesTests
{
    [Test]
    public async Task EffectiveChance_ScalesConfigByWorldRate()
    {
        await Assert.That(CharacterDurabilityRules.EffectiveChance(1.0f, 0.8f)).IsEqualTo(0.8f / 100f);
        await Assert.That(CharacterDurabilityRules.EffectiveChance(0.5f, 0.8f)).IsEqualTo(0.5f * 0.8f / 100f);
        await Assert.That(CharacterDurabilityRules.EffectiveChance(0f, 0.8f)).IsEqualTo(0f);
        await Assert.That(CharacterDurabilityRules.EffectiveChance(1.0f, 0f)).IsEqualTo(0f);
        await Assert.That(CharacterDurabilityRules.EffectiveChance(-1f, 0.8f)).IsEqualTo(1f);
    }

    [Test]
    public async Task ShouldLose_UsesProbabilityAndForceFlag()
    {
        await Assert.That(CharacterDurabilityRules.ShouldLose(-1f, 0.8f, 0.99)).IsTrue();
        await Assert.That(CharacterDurabilityRules.ShouldLose(1.0f, 0.8f, 0.007)).IsTrue();
        await Assert.That(CharacterDurabilityRules.ShouldLose(1.0f, 0.8f, 0.009)).IsFalse();
        await Assert.That(CharacterDurabilityRules.ShouldLose(0f, 0.8f, 0.0)).IsFalse();
    }

    [Test]
    public async Task ApplyLoss_ClampsAtZero()
    {
        await Assert.That(CharacterDurabilityRules.ApplyLoss(5, 1)).IsEqualTo((byte)4);
        await Assert.That(CharacterDurabilityRules.ApplyLoss(1, 1)).IsEqualTo((byte)0);
        await Assert.That(CharacterDurabilityRules.ApplyLoss(0, 1)).IsEqualTo((byte)0);
        await Assert.That(CharacterDurabilityRules.ApplyLoss(5, 0)).IsEqualTo((byte)5);
    }

    [Test]
    public async Task ArmorWorldRate_OnlyCombatKillReasons()
    {
        await Assert.That(CharacterDurabilityRules.ArmorWorldRate(
            KillReason.Damage, false, 0.5f, 1.0f)).IsEqualTo(1.0f);
        await Assert.That(CharacterDurabilityRules.ArmorWorldRate(
            KillReason.Damage, true, 0.5f, 1.0f)).IsEqualTo(0.5f);
        await Assert.That(CharacterDurabilityRules.ArmorWorldRate(
            KillReason.PvpEnemy, false, 0.5f, 1.0f)).IsEqualTo(0.5f);
        await Assert.That(CharacterDurabilityRules.ArmorWorldRate(
            KillReason.Fall, false, 0.5f, 1.0f)).IsEqualTo(0f);
        await Assert.That(CharacterDurabilityRules.ArmorWorldRate(
            KillReason.Drowning, true, 0.5f, 1.0f)).IsEqualTo(0f);
    }

    [Test]
    public async Task WeaponTarget_PrefersTheWeaponFlagThatFired()
    {
        await Assert.That(CharacterDurabilityRules.WeaponTarget(true, false, false))
            .IsEqualTo(DurabilityLossTargets.PrimaryWeapon);
        await Assert.That(CharacterDurabilityRules.WeaponTarget(false, true, false))
            .IsEqualTo(DurabilityLossTargets.SecondaryWeapon);
        await Assert.That(CharacterDurabilityRules.WeaponTarget(false, false, true))
            .IsEqualTo(DurabilityLossTargets.RangedWeapon);
        await Assert.That(CharacterDurabilityRules.WeaponTarget(false, false, false))
            .IsEqualTo(DurabilityLossTargets.AllWeapons);
    }
}
