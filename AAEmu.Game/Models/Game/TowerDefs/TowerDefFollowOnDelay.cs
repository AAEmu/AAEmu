namespace AAEmu.Game.Models.Game.TowerDefs;

/// <summary>
/// Delay before starting a configured follow-on tower after the predecessor's final prog opens.
/// </summary>
/// <remarks>
/// Uses the final step's <c>cond_to_next_time</c> (e.g. Abyssal 36 step 183 → 10 s before reward 37).
/// Zero / missing means start immediately (previous behavior).
/// </remarks>
public static class TowerDefFollowOnDelay
{
    public static TimeSpan FromFinalProg(TowerDefProg finalProg)
    {
        if (finalProg == null || finalProg.CondToNextTime <= 0f)
            return TimeSpan.Zero;
        return TimeSpan.FromSeconds(finalProg.CondToNextTime);
    }
}
