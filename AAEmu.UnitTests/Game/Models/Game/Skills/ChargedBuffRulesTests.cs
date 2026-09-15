using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// <c>damage_effects</c> carries two charged-buff column pairs, and a row may set both: the caster's
/// <c>charged_buff_id</c> / <c>charged_mul</c> / <c>charged_level_mul</c> and the target's
/// <c>target_charged_buff_id</c> / <c>target_charged_mul</c>. The target branch read the caster pair,
/// which zeroed its bonus on every row whose <c>charged_buff_id</c> is NULL and mispriced the rows
/// whose two multipliers differ.
/// </summary>
/// <remarks>content: 10.0.2.13 game_decrypted, damage_effects rows 3462 / 3602 / 4249 / 4456 / 4520 /
/// 5340 / 7140 / 7674 are the 8 with <c>use_target_charged_buff='t'</c>; <c>charged_level_mul</c> is
/// 0.0 on all of them.</remarks>
public class ChargedBuffRulesTests
{
    [Test]
    public async Task TargetBranch_NamesTheBuffTheTargetCarries()
    {
        // damage_effects 3602 (and 3462): charged_buff_id NULL with charged_mul 1.0, and
        // target_charged_buff_id 3930 검은 비늘 군단의 낙인 with target_charged_mul 60.
        var target = ChargedBuffRules.TargetBranch(3930, 60f);

        await Assert.That(target.BuffId).IsEqualTo(3930u);
        await Assert.That(target.PerChargeMultiplier).IsEqualTo(60f);
    }

    [Test]
    public async Task TargetBranch_PaysTheTargetMultiplierWhenBothPairsNameTheSameBuff()
    {
        // damage_effects 4249 / 5340: buff 899 누적 피해 on both sides, charged_mul 50 against
        // target_charged_mul 100. Reading the caster pair halved the charge bonus.
        var target = ChargedBuffRules.TargetBranch(899, 100f);

        await Assert.That(target.BuffId).IsEqualTo(899u);
        await Assert.That(target.PerChargeMultiplier).IsEqualTo(100f);
    }

    [Test]
    public async Task CasterBranch_KeepsTheChargedLevelMulTerm()
    {
        // The caster branch is unchanged: charged_level_mul scales with the skill's level.
        await Assert.That(ChargedBuffRules.CasterBranch(899, 50f, 0f, 11).PerChargeMultiplier).IsEqualTo(50f);
        await Assert.That(ChargedBuffRules.CasterBranch(899, 50f, 5f, 11).PerChargeMultiplier).IsEqualTo(105f);
        await Assert.That(ChargedBuffRules.CasterBranch(899, 50f, 5f, 11).BuffId).IsEqualTo(899u);
    }

    [Test]
    public async Task TargetBranch_TakesNoLevelTerm()
    {
        // damage_effects has a single level column and it belongs to the caster pair, so a target-side
        // charge is priced flat: 7140's 50 is 50 whatever level the cast is. There is no target-side
        // level column to read, and borrowing the caster's would price target content with caster data.
        await Assert.That(ChargedBuffRules.TargetBranch(899, 50f).PerChargeMultiplier).IsEqualTo(50f);
        await Assert.That(ChargedBuffRules.TargetBranch(15594, 1f).PerChargeMultiplier).IsEqualTo(1f);
    }
}
