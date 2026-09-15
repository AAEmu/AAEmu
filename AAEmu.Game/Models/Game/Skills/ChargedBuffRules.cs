namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// The two charged-buff branches of a <c>DamageEffect</c>. <c>damage_effects</c> carries two
/// independent column pairs and a row may set both: <c>use_charged_buff</c> reads
/// <c>charged_buff_id</c> / <c>charged_mul</c> off the caster, and <c>use_target_charged_buff</c>
/// reads <c>target_charged_buff_id</c> / <c>target_charged_mul</c> off the target.
/// </summary>
/// <remarks>
/// content, 10.0.2.13 game_decrypted: 8 rows have <c>use_target_charged_buff='t'</c>. Four of them
/// (3462, 3602, 4456, 4520) leave <c>charged_buff_id</c> NULL with <c>charged_mul</c> 1.0 and name a
/// real target buff — 3462/3602 target_charged_buff_id 3930 (검은 비늘 군단의 낙인, max_charge 30)
/// with target_charged_mul 60, 4456/4520 target 5372 with the same 60 — and each is referenced by one
/// enabled skill_effect (18533 참수, 18825/21144 고드프리의 참수, 21258 참수). 4249/5340 name buff 899
/// (누적 피해, max_charge 1000) on both sides with charged_mul 50 against target_charged_mul 100, and
/// 7140 names 899/50 on both. The eighth, 7674, is unreachable: target buff 15594 with
/// target_charged_mul 1.0 and <c>use_fixed_damage='t'</c>, and no <c>skill_effects</c> or
/// <c>buff_triggers</c> row names it. Rows 4249, 5340 and 7140 are reached through
/// <c>buff_triggers</c> rather than <c>skill_effects</c>, so a trigger with no skill uses the target
/// branch and must not be gated on <c>source.Skill</c>.
/// </remarks>
public static class ChargedBuffRules
{
    /// <summary>
    /// Buff a branch counts charges from, and the multiplier one charge contributes.
    /// </summary>
    public readonly record struct ChargeSource(uint BuffId, float PerChargeMultiplier);

    /// <summary>
    /// Caster branch: the caster's own buff and its <c>charged_level_mul</c> term.
    /// </summary>
    public static ChargeSource CasterBranch(uint chargedBuffId, float chargedMul, float chargedLevelMul, int skillLevel) =>
        new(chargedBuffId, chargedMul + skillLevel * chargedLevelMul);

    /// <summary>
    /// Target branch: the target's buff, priced flat at <c>target_charged_mul</c>.
    /// </summary>
    /// <remarks>
    /// <c>damage_effects</c> has one level column and it belongs to the caster pair
    /// (<c>use_charged_buff</c> / <c>charged_mul</c> / <c>charged_level_mul</c>), so there is no
    /// target-side level scaling to read. The term is deliberately not carried over: applying the
    /// caster's <c>charged_level_mul</c> here would price a target-side charge with caster-side
    /// content, which is the same mix-up this pair of methods exists to prevent. It is also inert
    /// today — <c>charged_level_mul</c> is 0.0 on all 8 rows — so nothing observable changes either
    /// way, and the flat read is the one the columns describe.
    /// </remarks>
    public static ChargeSource TargetBranch(uint targetChargedBuffId, float targetChargedMul) =>
        new(targetChargedBuffId, targetChargedMul);
}
