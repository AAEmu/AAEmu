using AAEmu.Game.Models.Game.Indun;

namespace AAEmu.UnitTests.Game.Models.Game.Indun;

public class IndunOpeningRulesTests
{
    [Test]
    public async Task Joined_DuringReady_SendsReadyOnly()
    {
        var step = IndunOpeningRules.Next(IndunOpeningStage.Joined, IndunInstancePhase.Ready);

        await Assert.That(step.SendReady).IsTrue();
        await Assert.That(step.SendStart).IsFalse();
        await Assert.That(step.Stage).IsEqualTo(IndunOpeningStage.Ready);
    }

    [Test]
    public async Task Joined_AfterReady_SendsReadyThenStart()
    {
        // A player who joined at the very end of the ready window still walks both steps,
        // because the client accepts start only from the ready state.
        var step = IndunOpeningRules.Next(IndunOpeningStage.Joined, IndunInstancePhase.Play);

        await Assert.That(step.SendReady).IsTrue();
        await Assert.That(step.SendStart).IsTrue();
        await Assert.That(step.Stage).IsEqualTo(IndunOpeningStage.Started);
    }

    [Test]
    public async Task Ready_DuringReady_SendsNothing()
    {
        var step = IndunOpeningRules.Next(IndunOpeningStage.Ready, IndunInstancePhase.Ready);

        await Assert.That(step.SendReady).IsFalse();
        await Assert.That(step.SendStart).IsFalse();
        await Assert.That(step.Stage).IsEqualTo(IndunOpeningStage.Ready);
    }

    [Test]
    [Arguments(IndunInstancePhase.Play)]
    [Arguments(IndunInstancePhase.End)]
    public async Task Ready_AfterReady_SendsStart(IndunInstancePhase phase)
    {
        var step = IndunOpeningRules.Next(IndunOpeningStage.Ready, phase);

        await Assert.That(step.SendReady).IsFalse();
        await Assert.That(step.SendStart).IsTrue();
        await Assert.That(step.Stage).IsEqualTo(IndunOpeningStage.Started);
    }

    [Test]
    [Arguments(IndunInstancePhase.Ready)]
    [Arguments(IndunInstancePhase.Play)]
    public async Task Started_SendsNothing(IndunInstancePhase phase)
    {
        var step = IndunOpeningRules.Next(IndunOpeningStage.Started, phase);

        await Assert.That(step.SendReady).IsFalse();
        await Assert.That(step.SendStart).IsFalse();
        await Assert.That(step.Stage).IsEqualTo(IndunOpeningStage.Started);
    }

    [Test]
    public async Task UsesOpening_OnlyWithAReadyWindow()
    {
        await Assert.That(IndunOpeningRules.UsesOpening(new IndunZoneOption(127, 10, 1000, 60))).IsTrue();
        await Assert.That(IndunOpeningRules.UsesOpening(new IndunZoneOption(127, 0, 1000, 60))).IsFalse();
        await Assert.That(IndunOpeningRules.UsesOpening(default)).IsFalse();
    }
}
