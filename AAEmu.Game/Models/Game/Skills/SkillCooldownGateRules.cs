namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// When a cast has to wait for the skill's own cooldown. The cooldown is armed on cast
/// (<see cref="Skill.Cast"/> and the plot-only fire edge) and is what the client's icon shows; before
/// this gate existed the player cast path only checked the short anti-spam delay and the shared global
/// cooldown, so any skill could be fired again the moment those allowed it.
/// </summary>
public static class SkillCooldownGateRules
{
    /// <summary>
    /// Basic attacks (2 melee / 3 offhand / 4 ranged — the same ids CSStartSkillPacket, SkillManager and
    /// Skill already special-case) are paced by the swing timer and the short
    /// anti-spam delay; their cooldown column is the swing interval, not a lockout. Gating them here
    /// stopped the client's auto-attack retries and made the hotbar feel unresponsive.
    /// </summary>
    public static bool IsBasicAttack(uint skillId) => skillId is 2 or 3 or 4;

    /// <summary>
    /// Simulation-driven casts (NPC AI, plot tasks, auto-attack tasks) pass <c>bypassGcd</c> and own
    /// their pacing, and sports-fishing hold/reel skills are re-pressed while their plot runs.
    /// </summary>
    public static bool ShouldWaitForCooldown(bool bypassGcd, bool fishingHold, uint skillId, bool hasActiveCooldown)
        => !bypassGcd && !fishingHold && !IsBasicAttack(skillId) && hasActiveCooldown;

    /// <summary>
    /// The non-zero cooldown tags a skill carries, in DB column order and without duplicates.
    /// </summary>
    /// <remarks>
    /// 295 of the 534 ability skills carry <c>cooldown_tag_id</c>; the tag is how the client groups the
    /// variants of one action (10534 빛과 어둠 / 36630 빛과 어둠: 생명 / 36631 빛과 어둠: 지진 all carry 3317)
    /// so that using one greys out the others. <c>second_cooldown_tag_id</c> is set on one shipped row
    /// and <c>third_cooldown_tag_id</c> on none, but both are read because the columns exist and a tag
    /// list that silently dropped them would arm the wrong set.
    /// </remarks>
    public static int[] CooldownTags(int firstTagId, int secondTagId, int thirdTagId)
    {
        var tags = new List<int>(3);
        foreach (var tagId in new[] { firstTagId, secondTagId, thirdTagId })
        {
            if (tagId > 0 && !tags.Contains(tagId))
                tags.Add(tagId);
        }

        return tags.Count == 0 ? [] : tags.ToArray();
    }

    /// <summary>
    /// Whether a running cooldown blocks this cast. The skill's own cooldown always does; a shared tag
    /// cooldown does not for a <c>switch_to_skill_cooldown</c> variant, because that variant is the
    /// second half of the very cast that armed the tag — 빛과 어둠 (10534, tag 3317) is followed
    /// immediately by whichever of 빛과 어둠: 생명 / 지진 (36630/36631, same tag) the player picks, and
    /// gating those on the tag would make the choice unpressable for the whole cooldown.
    /// </summary>
    public static bool CooldownBlocksCast(bool switchToSkillCooldown, bool ownCooldownActive, bool tagCooldownActive)
        => ownCooldownActive || (!switchToSkillCooldown && tagCooldownActive);

    /// <summary>
    /// Duration to arm when a skill with <c>switch_to_skill_cooldown</c> is used, given how much of the
    /// shared tag cooldown is left. The variant inherits the running family cooldown instead of starting
    /// a fresh one of its own length, which for 36632 연속 회복: 번개 (0 ms) would otherwise be no
    /// cooldown at all.
    /// </summary>
    public static uint SwitchToCooldownDuration(uint ownCooldownTime, TimeSpan sharedTagRemaining)
    {
        var shared = sharedTagRemaining > TimeSpan.Zero ? (uint)Math.Ceiling(sharedTagRemaining.TotalMilliseconds) : 0u;
        return Math.Max(ownCooldownTime, shared);
    }
}
