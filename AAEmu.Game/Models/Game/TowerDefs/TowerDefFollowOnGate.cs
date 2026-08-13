namespace AAEmu.Game.Models.Game.TowerDefs;

/// <summary>
/// Guards delayed follow-on starts so a stale timer cannot fire against a restarted run.
/// </summary>
public static class TowerDefFollowOnGate
{
    /// <summary>
    /// True when the live run still owns the same follow-on schedule generation.
    /// </summary>
    public static bool ShouldFire(
        uint pendingFollowOnId,
        uint expectedFollowOnId,
        ulong liveGeneration,
        ulong scheduledGeneration)
    {
        if (expectedFollowOnId == 0 || pendingFollowOnId == 0)
            return false;
        if (pendingFollowOnId != expectedFollowOnId)
            return false;
        return liveGeneration == scheduledGeneration;
    }
}
