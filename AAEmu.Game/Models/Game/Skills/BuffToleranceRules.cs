using AAEmu.Game.Models.Game.Skills.Buffs;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// What one application of a tolerance-tagged buff does to that family's step counter.
/// </summary>
/// <remarks>
/// Diminishing returns on crowd control: each CC of the same family inside
/// <c>buff_tolerances.step_duration</c> lands one step further down the ladder with its duration cut,
/// and the ladder's last step is the family's immunity step - the CC that arrives there is refused and
/// <c>final_step_buff_id</c> goes on instead.
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
    /// The application lands one step further down the ladder: shorter duration, closer to immunity.
    /// </summary>
    Advanced,

    /// <summary>
    /// This application arrives at the ladder's last step. The CC is refused, <c>final_step_buff_id</c>
    /// is applied in its place, and the counter restarts at the first step.
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
    /// <summary>
    /// False when the family refuses this buff, which is both the already-immune case and the
    /// application that reaches the ladder's immunity step.
    /// </summary>
    public bool Applies => Outcome
        is BuffToleranceOutcome.Started
        or BuffToleranceOutcome.Advanced
        or BuffToleranceOutcome.Untracked;
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
        if (tolerance == null)
            return new BuffToleranceDecision(BuffToleranceOutcome.Untracked, null);

        // The immunity outranks the ladder, and it outranks a ladder whose steps are missing: a CC the
        // family is already immune to must not land either way, and it must not advance the counter
        // either, or the immunity would end with the ladder part-way down instead of fresh.
        if (immunityBuffPresent)
            return new BuffToleranceDecision(BuffToleranceOutcome.Immune, counter?.CurrentStep);

        var steps = tolerance.Steps;
        if (steps == null || steps.Count == 0)
            return new BuffToleranceDecision(BuffToleranceOutcome.Untracked, null);

        var firstStep = tolerance.GetFirstStep();
        if (counter?.CurrentStep == null || !steps.Contains(counter.CurrentStep))
            return new BuffToleranceDecision(BuffToleranceOutcome.Started, firstStep);

        if (now > counter.LastStep + TimeSpan.FromSeconds(tolerance.StepDuration))
            return new BuffToleranceDecision(BuffToleranceOutcome.Started, firstStep);

        // The ladder is walked by position, not by comparing reductions, and its last step is the
        // family's immunity step rather than another landing step. Every one of the 31 shipped ladders
        // ends on a 0, 21 of them are all zeroes (0/0, and tolerance 43's 0/0/0), and reading that
        // trailing 0 as a landing step makes the CC before the immunity the longest of the burst - 0 %,
        // 25 %, 50 %, then 0 % again. GetStepAfter clamps at the last step, so "the next step is the
        // last one" covers both arriving there and finding the counter already on it.
        var nextStep = tolerance.GetStepAfter(counter.CurrentStep);
        if (nextStep == null || nextStep.Id >= steps[^1].Id)
            return new BuffToleranceDecision(BuffToleranceOutcome.ImmunityApplied, firstStep);

        return new BuffToleranceDecision(BuffToleranceOutcome.Advanced, nextStep);
    }
}
