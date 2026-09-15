using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillCooldownGateRulesTests
{
    [Test]
    public async Task PlayerSkill_WithAnArmedCooldown_Waits()
    {
        await Assert.That(SkillCooldownGateRules.ShouldWaitForCooldown(
            bypassGcd: false, fishingHold: false, skillId: 10025, hasActiveCooldown: true)).IsTrue();
    }

    [Test]
    public async Task PlayerSkill_WithNoCooldown_Casts()
    {
        await Assert.That(SkillCooldownGateRules.ShouldWaitForCooldown(
            bypassGcd: false, fishingHold: false, skillId: 10025, hasActiveCooldown: false)).IsFalse();
    }

    [Test]
    public async Task BasicAttacks_KeepTheClientSideSwingPacing()
    {
        foreach (var skillId in new uint[] { 2, 3, 4 })
        {
            await Assert.That(SkillCooldownGateRules.IsBasicAttack(skillId)).IsTrue();
            // Their cooldown column is the swing interval (2 = 300 ms, 4 = 500 ms); gating it made the
            // hotbar stop auto-attack retries.
            await Assert.That(SkillCooldownGateRules.ShouldWaitForCooldown(
                bypassGcd: false, fishingHold: false, skillId: skillId, hasActiveCooldown: true)).IsFalse();
        }
    }

    [Test]
    public async Task SimulationCasts_AndFishingHolds_AreNotGated()
    {
        // NPC AI, plot tasks and auto-attack tasks pass bypassGcd and own their own pacing.
        await Assert.That(SkillCooldownGateRules.ShouldWaitForCooldown(
            bypassGcd: true, fishingHold: false, skillId: 10025, hasActiveCooldown: true)).IsFalse();
        // Hold/reel kits re-press their skill while the plot runs.
        await Assert.That(SkillCooldownGateRules.ShouldWaitForCooldown(
            bypassGcd: false, fishingHold: true, skillId: 21571, hasActiveCooldown: true)).IsFalse();
    }
}
