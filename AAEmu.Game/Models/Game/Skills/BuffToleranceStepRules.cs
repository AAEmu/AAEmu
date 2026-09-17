using AAEmu.Game.Models.Game.Skills.Buffs;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Where the change_buff_tolerance_step special effect (type 157) moves a CC family's ladder.
/// </summary>
/// <remarks>
/// Nineteen <c>special_effects</c> rows carry type 157 and one of them is reached by a skill:
/// 39373, on 40364 결투를 위하여. That cast heals, restores mana, resets three cooldown tags and clears a
/// debuff tag - a "put everything back to a fresh state" skill - and its values are (0, 0). The other
/// eighteen are 0/0 as well except 33126 at (0, 1), none of which any skill uses. Neither value is a
/// <c>buff_tolerances.id</c> (the 31 families are 1, 4, 5, 6, 7, 9, …) nor a <c>buff_tolerance_steps.id</c>
/// (2, 3, 4, 5 for the first family, 6-9 for the next, …), so they read as a selector and a position
/// rather than as row ids: 0 selects every family the owner is tracking, and the second value is the
/// ladder position to move to, 0 being the first rung.
/// <para>
/// Which value is which cannot be told apart from shipped content, because every value a skill reaches is
/// 0. The order follows the rest of the table, where value1 is the primary parameter (reset_cooldown on
/// the same skill passes its tag in value2 only because value1 is its unused skill id).
/// </para>
/// <para>
/// The write is a placement, not a nudge: the counter is left claiming that it reached the new rung
/// just now, which is what keeps "the counter is on step N" and "it got there at LastStep" consistent.
/// Writing the rung without the timestamp would either expire the window mid-ladder or report a
/// reduction the owner never earned.
/// </para>
/// </remarks>
public static class BuffToleranceStepRules
{
    /// <summary>
    /// The rung a counter moves to for <paramref name="stepIndex"/>, clamped to the family's ladder
    /// (a family with no steps has nothing to place).
    /// </summary>
    public static BuffToleranceStep StepAt(BuffTolerance tolerance, int stepIndex)
    {
        var steps = tolerance?.Steps;
        if (steps == null || steps.Count == 0)
            return null;

        return steps[Math.Clamp(stepIndex, 0, steps.Count - 1)];
    }

    /// <summary>
    /// Whether a counter belongs to the family the effect names. Zero is the shipped row's value and
    /// means every family the owner currently tracks.
    /// </summary>
    public static bool SelectsFamily(uint counterToleranceId, int requestedToleranceId)
        => requestedToleranceId <= 0 || counterToleranceId == (uint)requestedToleranceId;
}
