using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class BuffToleranceRulesTests
{
    /// <summary>
    /// The ten four-step ladders as content ships them, tolerance 4 among them: buff_tolerances 4
    /// (buff_tag_id 6, step_duration 30, final_step_buff_id 2371) with buff_tolerance_steps 0 / 25 / 50 / 0.
    /// </summary>
    private static readonly uint[] FourStepLadder = [0, 25, 50, 0];

    /// <summary>The twenty two-step ladders, all of them 0 / 0.</summary>
    private static readonly uint[] TwoStepLadder = [0, 0];

    /// <summary>Tolerance 43 is the only three-step ladder and is all zeroes.</summary>
    private static readonly uint[] ThreeStepLadder = [0, 0, 0];

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

    #region Untracked

    [Test]
    public async Task Untracked_WithoutSteps_AppliesTheBuffAndKeepsNoCounter()
    {
        var tolerance = CreateTolerance();

        var decision = BuffToleranceRules.Decide(tolerance, null, false, DateTime.UtcNow);

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
    public async Task Untracked_WithoutATolerance_IsNotImmune()
    {
        // Immunity is a property of a family, so without a tolerance there is none to consult: the
        // immunity check sits behind the tolerance lookup, not in front of it.
        var decision = BuffToleranceRules.Decide(null, null, true, DateTime.UtcNow);

        await Assert.That(decision.Outcome).IsEqualTo(BuffToleranceOutcome.Untracked);
        await Assert.That(decision.Applies).IsTrue();
    }

    [Test]
    public async Task Immune_WithAToleranceThatListsNoSteps_StillRefuses()
    {
        // An empty step list is loader output too, and it must not turn the immunity into a pass: the
        // immunity outranks the ladder whether or not the ladder has steps to count.
        var tolerance = CreateTolerance();

        var decision = BuffToleranceRules.Decide(tolerance, null, true, DateTime.UtcNow);

        await Assert.That(decision.Outcome).IsEqualTo(BuffToleranceOutcome.Immune);
        await Assert.That(decision.Applies).IsFalse();
    }

    #endregion

    #region Started

    [Test]
    public async Task Started_WithoutACounter_OpensTheWindowAtTheFirstStep()
    {
        var tolerance = CreateTolerance(FourStepLadder);

        var decision = BuffToleranceRules.Decide(tolerance, null, false, DateTime.UtcNow);

        await Assert.That(decision.Outcome).IsEqualTo(BuffToleranceOutcome.Started);
        await Assert.That(decision.Step).IsSameReferenceAs(tolerance.Steps[0]);
        await Assert.That(decision.Applies).IsTrue();
    }

    [Test]
    public async Task Started_OnACounterWithNoStep_OpensTheWindowAtTheFirstStep()
    {
        var tolerance = CreateTolerance(FourStepLadder);
        var counter = new BuffToleranceCounter { Tolerance = tolerance, LastStep = DateTime.UtcNow };

        var decision = BuffToleranceRules.Decide(tolerance, counter, false, DateTime.UtcNow);

        await Assert.That(decision.Outcome).IsEqualTo(BuffToleranceOutcome.Started);
        await Assert.That(decision.Step).IsSameReferenceAs(tolerance.Steps[0]);
    }

    [Test]
    public async Task Started_OnACounterHoldingAStepThisToleranceNoLongerHas_StartsOver()
    {
        // Content can be reloaded under a counter that is already on a unit.
        var tolerance = CreateTolerance(FourStepLadder);
        var counter = new BuffToleranceCounter
        {
            Tolerance = tolerance,
            CurrentStep = new BuffToleranceStep { Id = 999, TimeReduction = 90 },
            LastStep = DateTime.UtcNow
        };

        var decision = BuffToleranceRules.Decide(tolerance, counter, false, DateTime.UtcNow);

        await Assert.That(decision.Outcome).IsEqualTo(BuffToleranceOutcome.Started);
        await Assert.That(decision.Step).IsSameReferenceAs(tolerance.Steps[0]);
    }

    [Test]
    public async Task Started_AfterTheStepWindow_RestartsAtTheFirstStep()
    {
        var tolerance = CreateTolerance(FourStepLadder);
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
        var tolerance = CreateTolerance(FourStepLadder);
        var now = DateTime.UtcNow;
        var counter = CounterAt(tolerance, 0, now.AddSeconds(-30));

        var decision = BuffToleranceRules.Decide(tolerance, counter, false, now);

        await Assert.That(decision.Outcome).IsEqualTo(BuffToleranceOutcome.Advanced);
        await Assert.That(decision.Step).IsSameReferenceAs(tolerance.Steps[1]);
    }

    #endregion

    #region Advanced

    [Test]
    public async Task Advanced_InsideTheWindow_MovesOneStepDownTheLadder()
    {
        var tolerance = CreateTolerance(FourStepLadder);
        var now = DateTime.UtcNow;
        var counter = CounterAt(tolerance, 1, now);

        var decision = BuffToleranceRules.Decide(tolerance, counter, false, now);

        await Assert.That(decision.Outcome).IsEqualTo(BuffToleranceOutcome.Advanced);
        await Assert.That(decision.Step).IsSameReferenceAs(tolerance.Steps[2]);
        await Assert.That(decision.Step.TimeReduction).IsEqualTo(50u);
    }

    [Test]
    public async Task Advanced_OnAnAllZeroLadder_WalksByPositionNotByReduction()
    {
        // Tolerance 43 is 0 / 0 / 0. Comparing reductions would call the second application the immunity
        // step and its third step could never be reached, so the ladder is walked by position instead.
        var tolerance = CreateTolerance(ThreeStepLadder);
        var now = DateTime.UtcNow;
        var counter = CounterAt(tolerance, 0, now);

        var decision = BuffToleranceRules.Decide(tolerance, counter, false, now);

        await Assert.That(decision.Outcome).IsEqualTo(BuffToleranceOutcome.Advanced);
        await Assert.That(decision.Step).IsSameReferenceAs(tolerance.Steps[1]);
    }

    #endregion

    #region ImmunityApplied

    [Test]
    public async Task ImmunityApplied_OnArrivingAtTheLastStep()
    {
        // The last step is the family's immunity step, not another landing step: 0 / 25 / 50 / 0 must
        // not run 0 %, 25 %, 50 %, then 0 % again with the immunity bolted on top of a full-length CC.
        var tolerance = CreateTolerance(FourStepLadder);
        var now = DateTime.UtcNow;
        var counter = CounterAt(tolerance, 2, now);

        var decision = BuffToleranceRules.Decide(tolerance, counter, false, now);

        await Assert.That(decision.Outcome).IsEqualTo(BuffToleranceOutcome.ImmunityApplied);
        await Assert.That(decision.Step).IsSameReferenceAs(tolerance.Steps[0]);
        await Assert.That(decision.Applies).IsFalse();
    }

    [Test]
    public async Task ImmunityApplied_OnArrivingAtTheSecondOfTwoSteps()
    {
        var tolerance = CreateTolerance(TwoStepLadder);
        var now = DateTime.UtcNow;
        var counter = CounterAt(tolerance, 0, now);

        var decision = BuffToleranceRules.Decide(tolerance, counter, false, now);

        await Assert.That(decision.Outcome).IsEqualTo(BuffToleranceOutcome.ImmunityApplied);
        await Assert.That(decision.Step).IsSameReferenceAs(tolerance.Steps[0]);
        await Assert.That(decision.Applies).IsFalse();
    }

    [Test]
    public async Task ImmunityApplied_OnArrivingAtTheThirdOfThreeSteps()
    {
        var tolerance = CreateTolerance(ThreeStepLadder);
        var now = DateTime.UtcNow;
        var counter = CounterAt(tolerance, 1, now);

        var decision = BuffToleranceRules.Decide(tolerance, counter, false, now);

        await Assert.That(decision.Outcome).IsEqualTo(BuffToleranceOutcome.ImmunityApplied);
        await Assert.That(decision.Step).IsSameReferenceAs(tolerance.Steps[0]);
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
    public async Task ImmunityApplied_OnACounterAlreadySittingOnTheLastStep()
    {
        // A counter left on the last step - by an older build or a save - is past the ladder, so it
        // hands the immunity over rather than advancing off the end.
        var tolerance = CreateTolerance(FourStepLadder);
        var now = DateTime.UtcNow;
        var counter = CounterAt(tolerance, 3, now);

        var decision = BuffToleranceRules.Decide(tolerance, counter, false, now);

        await Assert.That(decision.Outcome).IsEqualTo(BuffToleranceOutcome.ImmunityApplied);
        await Assert.That(decision.Step).IsSameReferenceAs(tolerance.Steps[0]);
    }

    #endregion

    #region Immune

    [Test]
    public async Task Immune_WhileTheFinalStepBuffIsUp_RefusesTheCcAndLeavesTheStepAlone()
    {
        var tolerance = CreateTolerance(FourStepLadder);
        var now = DateTime.UtcNow;
        var counter = CounterAt(tolerance, 1, now);

        var decision = BuffToleranceRules.Decide(tolerance, counter, true, now);

        await Assert.That(decision.Outcome).IsEqualTo(BuffToleranceOutcome.Immune);
        // The step handed back is the one already there, so a caller that writes it back cannot move
        // the ladder while the family is immune.
        await Assert.That(decision.Step).IsSameReferenceAs(tolerance.Steps[1]);
        await Assert.That(decision.Applies).IsFalse();
    }

    [Test]
    public async Task Immune_IsReportedEvenWithoutACounter()
    {
        // The immunity outranks the ladder: a CC arriving while the family's final-step buff is up must
        // not open a counter either.
        var tolerance = CreateTolerance(FourStepLadder);

        var decision = BuffToleranceRules.Decide(tolerance, null, true, DateTime.UtcNow);

        await Assert.That(decision.Outcome).IsEqualTo(BuffToleranceOutcome.Immune);
        await Assert.That(decision.Applies).IsFalse();
        await Assert.That(decision.Step).IsNull();
    }

    [Test]
    public async Task Immune_IsReportedEvenAfterTheStepWindow()
    {
        // A lapsed window is not licence to re-open the ladder while the immunity is still up.
        var tolerance = CreateTolerance(FourStepLadder);
        var now = DateTime.UtcNow;
        var counter = CounterAt(tolerance, 0, now.AddMinutes(-5));

        var decision = BuffToleranceRules.Decide(tolerance, counter, true, now);

        await Assert.That(decision.Outcome).IsEqualTo(BuffToleranceOutcome.Immune);
        await Assert.That(decision.Applies).IsFalse();
    }

    #endregion

    #region GetStepAfter

    [Test]
    public async Task GetStepAfter_PastTheEnd_ReturnsTheLastStep()
    {
        var tolerance = CreateTolerance(FourStepLadder);

        var next = tolerance.GetStepAfter(tolerance.Steps[3]);

        await Assert.That(next).IsSameReferenceAs(tolerance.Steps[3]);
    }

    [Test]
    public async Task GetStepAfter_InsideTheLadder_ReturnsTheFollowingStep()
    {
        var tolerance = CreateTolerance(FourStepLadder);

        var next = tolerance.GetStepAfter(tolerance.Steps[0]);

        await Assert.That(next).IsSameReferenceAs(tolerance.Steps[1]);
    }

    #endregion
}
