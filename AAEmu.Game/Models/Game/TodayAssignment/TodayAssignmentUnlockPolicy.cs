namespace AAEmu.Game.Models.Game.TodayAssignment;

/// <summary>
/// Slot unlocks persist for the character. Daily reset drops quest progress only;
/// previously unlocked slots come back Ready (blue), not Locked (green).
/// </summary>
public static class TodayAssignmentUnlockPolicy
{
    /// <summary>Any successful unlock (free or paid) is remembered for the character.</summary>
    public static bool GrantLifetimeOnUnlock => true;

    public static bool MustConsumeItemCost(bool isPaidStep, bool alreadyLifetimeUnlocked)
        => isPaidStep && !alreadyLifetimeUnlocked;

    /// <summary>
    /// New UTC day with no today-row yet: lifetime-unlocked → Ready, never unlocked → Locked.
    /// </summary>
    public static TodayAssignmentStatus StatusForNewDay(bool lifetimeUnlocked)
        => lifetimeUnlocked ? TodayAssignmentStatus.Ready : TodayAssignmentStatus.Locked;

    public static bool ShouldSeedReady(bool lifetimeUnlocked, bool hasTodayRow)
        => lifetimeUnlocked && !hasTodayRow;
}
