using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class BuffToleranceStepRulesTests
{
    /// <summary>
    /// buff_tolerances 4 (buff_tag_id 6, step_duration 30), whose four buff_tolerance_steps rows are
    /// 0 / 25 / 50 / 0 - the ladder the tolerance tests already use.
    /// </summary>
    private static BuffTolerance FourStepLadder()
    {
        var tolerance = new BuffTolerance
        {
            Id = 4,
            BuffTagId = 6,
            StepDuration = 30,
            FinalStepBuffId = 2371,
            Steps = []
        };

        var stepId = 6u;
        foreach (var timeReduction in new uint[] { 0, 25, 50, 0 })
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

    [Test]
    public async Task StepZero_IsTheFirstRung()
    {
        var tolerance = FourStepLadder();

        var step = BuffToleranceStepRules.StepAt(tolerance, 0);

        await Assert.That(step).IsSameReferenceAs(tolerance.Steps[0]);
        await Assert.That(step.TimeReduction).IsEqualTo(0u);
    }

    [Test]
    public async Task AStepIndexPicksThatRung()
    {
        var tolerance = FourStepLadder();

        // special_effects row 33126 (type 157) is the only one that is not 0/0 and passes 1.
        await Assert.That(BuffToleranceStepRules.StepAt(tolerance, 1)).IsSameReferenceAs(tolerance.Steps[1]);
        await Assert.That(BuffToleranceStepRules.StepAt(tolerance, 2).TimeReduction).IsEqualTo(50u);
    }

    [Test]
    public async Task AnIndexPastTheLadder_ClampsToTheLastRung()
    {
        var tolerance = FourStepLadder();

        await Assert.That(BuffToleranceStepRules.StepAt(tolerance, 9)).IsSameReferenceAs(tolerance.Steps[^1]);
    }

    [Test]
    public async Task ANegativeIndex_ClampsToTheFirstRung()
    {
        var tolerance = FourStepLadder();

        await Assert.That(BuffToleranceStepRules.StepAt(tolerance, -3)).IsSameReferenceAs(tolerance.Steps[0]);
    }

    [Test]
    public async Task AFamilyWithoutSteps_HasNowhereToMove()
    {
        await Assert.That(BuffToleranceStepRules.StepAt(null, 0)).IsNull();
        await Assert.That(BuffToleranceStepRules.StepAt(new BuffTolerance { Steps = [] }, 0)).IsNull();
        await Assert.That(BuffToleranceStepRules.StepAt(new BuffTolerance { Steps = null }, 0)).IsNull();
    }

    [Test]
    public async Task Zero_SelectsEveryFamilyTheOwnerTracks()
    {
        // The one row a skill uses (39373 on 40364 결투를 위하여) passes 0, and no buff_tolerances id is 0
        // (they start at 1), so zero has to mean "all of them".
        await Assert.That(BuffToleranceStepRules.SelectsFamily(1, 0)).IsTrue();
        await Assert.That(BuffToleranceStepRules.SelectsFamily(43, 0)).IsTrue();
    }

    [Test]
    public async Task ANamedFamily_SelectsOnlyThatCounter()
    {
        await Assert.That(BuffToleranceStepRules.SelectsFamily(6, 6)).IsTrue();
        await Assert.That(BuffToleranceStepRules.SelectsFamily(6, 7)).IsFalse();
    }
}
