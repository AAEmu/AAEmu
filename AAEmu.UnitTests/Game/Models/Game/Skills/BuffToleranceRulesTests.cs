using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class BuffToleranceRulesTests
{
    /// <summary>
    /// The stepped ladder as content ships it: buff_tolerances 4 (buff_tag_id 6, step_duration 30,
    /// final_step_buff_id 2371) with its four buff_tolerance_steps rows — 0 %, 25 %, 50 %, then a
    /// trailing 0 % step that is where the ladder stops paying out and the immunity is handed over.
    /// </summary>
    private static readonly uint[] LiveLadder = [0, 25, 50, 0];

    private static BuffTolerance CreateTolerance(params uint[] timeReductions)
    {
        var tolerance = new BuffTolerance
        {
            Id = 4,
            BuffTagId = 6,
            StepDuration = 30,
            FinalStepBuffId = 2371,
            CharacterTimeReduction = 0,
            Steps = []
        };

        var stepId = 6u;
        foreach (var timeReduction in timeReductions)
        {
            tolerance.Steps.Add(new BuffToleranceStep
            {
                Id = stepId++,
                BuffTolerance = tolerance,
                HitChance = 100,
                TimeReduction = timeReduction
            });
        }

        return tolerance;
    }

    private static BuffToleranceCounter CounterAt(BuffTolerance tolerance, int stepIndex, DateTime lastStep) =>
        new() { Tolerance = tolerance, CurrentStep = tolerance.Steps[stepIndex], LastStep = lastStep };

    [Test]
    public async Task Untracked_WithoutSteps_AppliesTheBuffAndKeepsNoCounter()
    {
        var tolerance = CreateTolerance();
        var now = DateTime.UtcNow;

        var decision = BuffToleranceRules.Decide(tolerance, null, false, now);

        await Assert.That(decision.Outcome).IsEqualTo(BuffToleranceOutcome.Untracked);
        await Assert.That(decision.Step).IsNull();
        await Assert.That(decision.Applies).IsTrue();
    }

    [Test]
    public async Task Untracked_WithoutATolerance_AppliesTheBuffAndKeepsNoCounter()
    {
        var decision = BuffToleranceRules.Decide(null, null, false, DateTime.UtcNow);

        await Assert.That(decision.Outcome).IsEqualTo(BuffToleranceOutcome.Untracked);
        await Assert.That(decision.Applies).IsTrue();
    }

    [Test]
    public async Task Started_WithoutACounter_OpensTheWindowAtTheFirstStep()
    {
        var tolerance = CreateTolerance(LiveLadder);

        var decision = BuffToleranceRules.Decide(tolerance, null, false, DateTime.UtcNow);

        await Assert.That(decision.Outcome).IsEqualTo(BuffToleranceOutcome.Started);
        await Assert.That(decision.Step).IsSameReferenceAs(tolerance.Steps[0]);
        await Assert.That(decision.Applies).IsTrue();
    }

    [Test]
    public async Task Started_OnACounterWithNoStep_OpensTheWindowAtTheFirstStep()
    {
        // A counter restored from a save with a step the content no longer lists.
        var tolerance = CreateTolerance(LiveLadder);
        var counter = new BuffToleranceCounter { Tolerance = tolerance, LastStep = DateTime.UtcNow };

        var decision = BuffToleranceRules.Decide(tolerance, counter, false, DateTime.UtcNow);

        await Assert.That(decision.Outcome).IsEqualTo(BuffToleranceOutcome.Started);
        await Assert.That(decision.Step).IsSameReferenceAs(tolerance.Steps[0]);
    }

    [Test]
    public async Task Started_AfterTheStepWindow_RestartsAtTheFirstStep()
    {
        var tolerance = CreateTolerance(LiveLadder);
        var now = DateTime.UtcNow;
        var counter = CounterAt(tolerance, 1, now.AddSeconds(-31));

        var decision = BuffToleranceRules.Decide(tolerance, counter, false, now);

        await Assert.That(decision.Outcome).IsEqualTo(BuffToleranceOutcome.Started);
        await Assert.That(decision.Step).IsSameReferenceAs(tolerance.Steps[0]);
    }

    [Test]
    public async Task Started_IsNotTriggered_OnTheStepWindowBoundary()
    {
        // The window is exclusive at its end: a CC landing exactly step_duration later still counts as
        // part of the same burst.
        var tolerance = CreateTolerance(LiveLadder);
        var now = DateTime.UtcNow;
        var counter = CounterAt(tolerance, 0, now.AddSeconds(-30));

        var decision = BuffToleranceRules.Decide(tolerance, counter, false, now);

        await Assert.That(decision.Outcome).IsEqualTo(BuffToleranceOutcome.Advanced);
        await Assert.That(decision.Step).IsSameReferenceAs(tolerance.Steps[1]);
    }

    [Test]
    public async Task Advanced_InsideTheWindow_MovesOneStepDownTheLadder()
    {
        var tolerance = CreateTolerance(LiveLadder);
        var now = DateTime.UtcNow;
        var counter = CounterAt(tolerance, 1, now);

        var decision = BuffToleranceRules.Decide(tolerance, counter, false, now);

        await Assert.That(decision.Outcome).IsEqualTo(BuffToleranceOutcome.Advanced);
        await Assert.That(decision.Step).IsSameReferenceAs(tolerance.Steps[2]);
        await Assert.That(decision.Step.TimeReduction).IsEqualTo(50u);
    }

    [Test]
    public async Task ImmunityApplied_OnTheTrailingStepThatReducesNoMore()
    {
        // Step 4 of the shipped ladder is 0 %, below the 50 % before it: the ladder is over, so the
        // counter restarts and the family's final-step immunity is handed out.
        var tolerance = CreateTolerance(LiveLadder);
        var now = DateTime.UtcNow;
        var counter = CounterAt(tolerance, 2, now);

        var decision = BuffToleranceRules.Decide(tolerance, counter, false, now);

        await Assert.That(decision.Outcome).IsEqualTo(BuffToleranceOutcome.ImmunityApplied);
        await Assert.That(decision.Step).IsSameReferenceAs(tolerance.Steps[0]);
        await Assert.That(decision.Applies).IsTrue();
    }

    [Test]
    public async Task ImmunityApplied_OnASingleStepLadder_InsteadOfThrowing()
    {
        // GetStepAfter clamps at the last step. Before it did, this call threw
        // InvalidOperationException out of First() on the application that reached the end.
        var tolerance = CreateTolerance(0);
        var now = DateTime.UtcNow;
        var counter = CounterAt(tolerance, 0, now);

        var decision = BuffToleranceRules.Decide(tolerance, counter, false, now);

        await Assert.That(decision.Outcome).IsEqualTo(BuffToleranceOutcome.ImmunityApplied);
        await Assert.That(decision.Step).IsSameReferenceAs(tolerance.Steps[0]);
    }

    [Test]
    public async Task Immune_WhileTheFinalStepBuffIsUp_RefusesTheCcAndLeavesTheStepAlone()
    {
        var tolerance = CreateTolerance(LiveLadder);
        var now = DateTime.UtcNow;
        var counter = CounterAt(tolerance, 1, now);

        var decision = BuffToleranceRules.Decide(tolerance, counter, true, now);

        await Assert.That(decision.Outcome).IsEqualTo(BuffToleranceOutcome.Immune);
        // The step handed back is the one already there, so a caller that writes it back cannot move
        // the ladder while the family is immune.
        await Assert.That(decision.Step).IsSameReferenceAs(tolerance.Steps[1]);
    }

    [Test]
    public async Task Immune_IsReportedEvenWithoutACounter()
    {
        // The immunity outranks the ladder: a CC arriving while the family's final-step buff is up must
        // not open a counter either.
        var tolerance = CreateTolerance(LiveLadder);

        var decision = BuffToleranceRules.Decide(tolerance, null, true, DateTime.UtcNow);

        await Assert.That(decision.Outcome).IsEqualTo(BuffToleranceOutcome.Immune);
        await Assert.That(decision.Applies).IsFalse();
        await Assert.That(decision.Step).IsNull();
    }

    [Test]
    public async Task Immune_IsReportedEvenAfterTheStepWindow()
    {
        // A lapsed window is not licence to re-open the ladder while the immunity is still up.
        var tolerance = CreateTolerance(LiveLadder);
        var now = DateTime.UtcNow;
        var counter = CounterAt(tolerance, 0, now.AddMinutes(-5));

        var decision = BuffToleranceRules.Decide(tolerance, counter, true, now);

        await Assert.That(decision.Outcome).IsEqualTo(BuffToleranceOutcome.Immune);
        await Assert.That(decision.Applies).IsFalse();
    }

    [Test]
    public async Task GetStepAfter_PastTheEnd_ReturnsTheLastStep()
    {
        var tolerance = CreateTolerance(LiveLadder);

        var next = tolerance.GetStepAfter(tolerance.Steps[3]);

        await Assert.That(next).IsSameReferenceAs(tolerance.Steps[3]);
    }

    [Test]
    public async Task GetStepAfter_InsideTheLadder_ReturnsTheFollowingStep()
    {
        var tolerance = CreateTolerance(LiveLadder);

        var next = tolerance.GetStepAfter(tolerance.Steps[0]);

        await Assert.That(next).IsSameReferenceAs(tolerance.Steps[1]);
    }
}
