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
}
