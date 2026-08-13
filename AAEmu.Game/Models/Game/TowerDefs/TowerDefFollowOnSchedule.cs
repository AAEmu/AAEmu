namespace AAEmu.Game.Models.Game.TowerDefs;

/// <summary>
/// Generation-gated follow-on arming for one predecessor run. Stale delayed wakes must not fire
/// after end/restart; the live generation is the only claim token.
/// </summary>
public sealed class TowerDefFollowOnSchedule
{
    public ulong Generation { get; private set; }
    public uint PendingFollowOnId { get; private set; }
    public ulong FollowOnGeneration { get; private set; }

    /// <summary>Begin or restart a run (new generation; clears any pending follow-on).</summary>
    public void BeginRun(ulong generation)
    {
        Generation = generation;
        ClearPending();
    }

    /// <summary>Arm a delayed follow-on against the current generation.</summary>
    public void Schedule(uint followOnId)
    {
        if (followOnId == 0 || Generation == 0)
            return;
        PendingFollowOnId = followOnId;
        FollowOnGeneration = Generation;
    }

    /// <summary>End the run without starting another (drops pending so stale delays no-op).</summary>
    public void EndRun() => ClearPending();

    /// <summary>
    /// Consume a due follow-on when the wake still matches this run. Returns false for stale
    /// generations or cleared pending.
    /// </summary>
    public bool TryConsumeDue(uint expectedFollowOnId, ulong scheduledGeneration)
    {
        if (!TowerDefFollowOnGate.ShouldFire(
                PendingFollowOnId, expectedFollowOnId, Generation, scheduledGeneration))
            return false;
        ClearPending();
        return true;
    }

    private void ClearPending()
    {
        PendingFollowOnId = 0;
        FollowOnGeneration = 0;
    }
}
