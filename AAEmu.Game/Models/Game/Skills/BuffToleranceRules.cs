using AAEmu.Game.Models.Game.Skills.Buffs;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// What one application of a tolerance-tagged buff does to that family's step counter.
/// </summary>
/// <remarks>
/// Diminishing returns on crowd control: each CC of the same family inside
/// <c>buff_tolerances.step_duration</c> lands a step further up the ladder with its duration cut, and
/// the step where the ladder stops paying out hands the owner the family's
/// <c>final_step_buff_id</c> immunity instead.
/// </remarks>
public enum BuffToleranceOutcome
{
    /// <summary>
    /// The family's final-step immunity buff is already on the owner. The incoming CC does not land and
    /// the counter stays exactly where it is: the immunity is what refuses the cast, so there is nothing
    /// left to count.
    /// </summary>
    Immune,

    /// <summary>
    /// No counter yet, or the last CC is older than <c>step_duration</c>. This application opens the
    /// window at the first step and lands at full duration.
    /// </summary>
    Started,

    /// <summary>
    /// The application lands one step deeper into the family: shorter duration, closer to immunity.
    /// </summary>
    Advanced,

    /// <summary>
    /// The ladder was already at its last step. The final-step immunity buff is handed out and the
    /// counter restarts at the first step.
    /// </summary>
    ImmunityApplied,

    /// <summary>
    /// The tolerance lists no steps, so there is nothing to count. The buff is applied normally and no
    /// counter is kept.
    /// </summary>
    Untracked
}

/// <summary>
/// The counter state to write after one application, and whether the incoming buff lands at all.
/// </summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Step">The step the counter ends on, or null when there is no counter to write.</param>
public readonly record struct BuffToleranceDecision(BuffToleranceOutcome Outcome, BuffToleranceStep Step)
{
    /// <summary>False only for <see cref="BuffToleranceOutcome.Immune"/>.</summary>
    public bool Applies => Outcome != BuffToleranceOutcome.Immune;
}

/// <summary>
/// The diminishing-returns ladder for one CC family (testable, no side effects).
/// </summary>
/// <remarks>
/// The reported failure was an <c>ArgumentException</c> out of <c>Buffs.AddBuff</c>: the old code
/// decided whether to advance an existing counter and whether to create one in the same condition, so
/// a CC arriving while its family's immunity buff was up fell through to the create branch and added a
/// key that was already there. That exception escaped the effect loop, which took the rest of the cast's
/// effects and its <c>EndSkill</c> with it. Deciding here keeps "the counter exists" and "the counter
/// advances" as separate answers.
/// </remarks>
public static class BuffToleranceRules
{
    /// <summary>
    /// Resolves one application against a family's tolerance counter.
    /// </summary>
    /// <param name="tolerance">The family's tolerance, or null when this buff carries no trodden tag.</param>
    /// <param name="counter">The owner's counter for this family, or null when none exists yet.</param>
    /// <param name="immunityBuffPresent">Whether <c>final_step_buff_id</c> is already on the owner.</param>
    /// <param name="now">Evaluation time.</param>
    public static BuffToleranceDecision Decide(
        BuffTolerance tolerance,
        BuffToleranceCounter counter,
        bool immunityBuffPresent,
        DateTime now)
    {
        if (tolerance?.Steps == null || tolerance.Steps.Count == 0)
            return new BuffToleranceDecision(BuffToleranceOutcome.Untracked, null);

        // The immunity outranks the ladder. A CC the family is already immune to must not advance the
        // counter either, or the immunity would end with the ladder part-way up instead of fresh.
        if (immunityBuffPresent)
            return new BuffToleranceDecision(BuffToleranceOutcome.Immune, counter?.CurrentStep);

        var firstStep = tolerance.GetFirstStep();
        if (counter?.CurrentStep == null)
            return new BuffToleranceDecision(BuffToleranceOutcome.Started, firstStep);

        if (now > counter.LastStep + TimeSpan.FromSeconds(tolerance.StepDuration))
            return new BuffToleranceDecision(BuffToleranceOutcome.Started, firstStep);

        // GetStepAfter clamps at the last step, so a next step that reduces no more than the current one
        // is how both "the ladder is exhausted" and the data's trailing zero-reduction step read.
        var nextStep = tolerance.GetStepAfter(counter.CurrentStep);
        if (nextStep == null || nextStep.TimeReduction <= counter.CurrentStep.TimeReduction)
            return new BuffToleranceDecision(BuffToleranceOutcome.ImmunityApplied, firstStep);

        return new BuffToleranceDecision(BuffToleranceOutcome.Advanced, nextStep);
    }
}
