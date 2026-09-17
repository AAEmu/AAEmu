using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

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

    [Test]
    public async Task CooldownTags_DropsZeroes_AndKeepsColumnOrder()
    {
        await Assert.That(SkillCooldownGateRules.CooldownTags(0, 0, 0)).IsEmpty();
        await Assert.That(SkillCooldownGateRules.CooldownTags(3317, 0, 0)).IsEquivalentTo(new[] { 3317 });
        await Assert.That(SkillCooldownGateRules.CooldownTags(3317, 3318, 0)).IsEquivalentTo(new[] { 3317, 3318 });
    }

    [Test]
    public async Task CooldownTags_KeepsOneCopyOfARepeatedTag()
    {
        // 36630 빛과 어둠: 생명 carries the same tag in two columns; arming it twice would be harmless
        // but the list is what the client-side family is keyed on, so it stays a set.
        await Assert.That(SkillCooldownGateRules.CooldownTags(3317, 0, 3317)).IsEquivalentTo(new[] { 3317 });
    }

    [Test]
    public async Task TagCooldown_BlocksASiblingOfTheSameTag()
    {
        // 10534 빛과 어둠 and 36631 빛과 어둠: 지진 share tag 3317.
        var cooldowns = new UnitCooldowns();
        var tags = SkillCooldownGateRules.CooldownTags(3317, 0, 0);

        cooldowns.AddCooldown(10534u, 3000u, tags);

        await Assert.That(cooldowns.CheckCooldown(36631u)).IsFalse();
        await Assert.That(cooldowns.CheckTagCooldown(tags)).IsTrue();
        await Assert.That(cooldowns.CheckCooldown(36631u, tags)).IsTrue();
    }

    [Test]
    public async Task SwitchToSkillCooldown_RidesTheFamilyCooldown_InsteadOfWaitingForIt()
    {
        // The parent arms the tag; the variant the player picks is pressed immediately afterwards.
        await Assert.That(SkillCooldownGateRules.CooldownBlocksCast(
            switchToSkillCooldown: true, ownCooldownActive: false, tagCooldownActive: true)).IsFalse();
        // A variant still waits for its own cooldown.
        await Assert.That(SkillCooldownGateRules.CooldownBlocksCast(
            switchToSkillCooldown: true, ownCooldownActive: true, tagCooldownActive: false)).IsTrue();
        // Everything else waits for either.
        await Assert.That(SkillCooldownGateRules.CooldownBlocksCast(
            switchToSkillCooldown: false, ownCooldownActive: false, tagCooldownActive: true)).IsTrue();
    }

    [Test]
    public async Task SwitchToCooldownDuration_TakesTheLongerOfOwnAndFamily()
    {
        // 36632 연속 회복: 번개 declares 0 ms; taking its own value would clear the family cooldown.
        await Assert.That(SkillCooldownGateRules.SwitchToCooldownDuration(
            0u, TimeSpan.FromMilliseconds(14600))).IsEqualTo(14600u);
        // A variant with a longer own cooldown keeps it.
        await Assert.That(SkillCooldownGateRules.SwitchToCooldownDuration(
            30000u, TimeSpan.FromMilliseconds(14600))).IsEqualTo(30000u);
        // No family cooldown running: the skill's own value stands.
        await Assert.That(SkillCooldownGateRules.SwitchToCooldownDuration(
            3000u, TimeSpan.Zero)).IsEqualTo(3000u);
    }
}
