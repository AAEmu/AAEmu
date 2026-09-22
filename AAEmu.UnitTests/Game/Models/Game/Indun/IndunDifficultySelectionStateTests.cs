using AAEmu.Game.Models.Game.Indun;

namespace AAEmu.UnitTests.Game.Models.Game.Indun;

public class IndunDifficultySelectionStateTests
{
    [Test]
    public async Task ReservedOpenerOwnsSelectionAndReplayIsIdempotent()
    {
        var state = new IndunDifficultySelectionState();
        var completions = 0;

        await Assert.That(state.Reserve(10, null, () => completions++)).IsTrue();
        await Assert.That(state.IsReservedBy(10)).IsTrue();
        await Assert.That(state.IsReservedBy(11)).IsFalse();
        await Assert.That(state.Select(11, null, 4)).IsEqualTo(IndunDifficultySelection.Unauthorized);
        await Assert.That(state.Select(10, null, 4)).IsEqualTo(IndunDifficultySelection.First);
        await Assert.That(state.Select(10, 4, 4)).IsEqualTo(IndunDifficultySelection.Same);
        await Assert.That(state.Select(10, 4, 9)).IsEqualTo(IndunDifficultySelection.Conflict);
        state.TakeCompletion(10)?.Invoke();
        await Assert.That(completions).IsEqualTo(1);
        var consumedCompletion = state.TakeCompletion(10);
        await Assert.That((object)consumedCompletion).IsNull();
    }

    [Test]
    public async Task LeaveReleasesUnfinishedSelection()
    {
        var state = new IndunDifficultySelectionState();
        state.Reserve(10, null, () => { });

        state.Release(10);

        await Assert.That(state.Reserve(11, null, () => { })).IsTrue();
    }

    [Test]
    public async Task InvalidChoiceKeepsReservationForValidRetry()
    {
        var state = new IndunDifficultySelectionState();
        byte? selected = null;
        var completions = 0;
        state.Reserve(10, selected, () => completions++);

        var invalid = state.TryApply(10, ref selected, 99, false, out _, out _);
        var valid = state.TryApply(10, ref selected, 4, true, out var completion, out var changed);
        completion?.Invoke();

        await Assert.That(invalid).IsFalse();
        await Assert.That(valid).IsTrue();
        await Assert.That(changed).IsTrue();
        await Assert.That(selected).IsEqualTo((byte)4);
        await Assert.That(completions).IsEqualTo(1);
    }
}
