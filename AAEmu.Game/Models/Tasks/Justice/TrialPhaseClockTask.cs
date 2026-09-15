using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Justice;

/// <summary>
/// One phase clock of a trial. It runs on the task manager's own thread like every other game timer,
/// so a phase that runs out while a packet is being handled cannot mutate the trial from a
/// thread-pool continuation. The token it carries is what makes a clock armed for an earlier phase go
/// quiet once that phase has ended early.
/// </summary>
public class TrialPhaseClockTask(ulong trialId, int phaseToken, Action onElapsed) : Task
{
    public override void Execute()
    {
        TrialManager.Instance.CompletePhaseClock(trialId, phaseToken, onElapsed);
    }
}
