namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// When an area sphere may fire again, from its <c>spheres.trigger_condition_id</c> and
/// <c>trigger_condition_time</c> (milliseconds). The sphere is one object in its world, so a firing
/// arms the condition for everyone who walks into it afterwards.
/// </summary>
public static class AreaSphereTriggerRules
{
    public static bool CanFire(AreaSphereTriggerCondition condition, uint intervalMs, DateTime? lastFiredUtc, DateTime nowUtc) =>
        condition switch
        {
            AreaSphereTriggerCondition.TriggerOnceAtAll or AreaSphereTriggerCondition.TriggerOnceInRuntime =>
                lastFiredUtc == null,
            AreaSphereTriggerCondition.TriggerEveryNTimeAfter =>
                lastFiredUtc == null || nowUtc - lastFiredUtc.Value >= TimeSpan.FromMilliseconds(intervalMs),
            _ => true
        };

    /// <summary>
    /// A sphere fires on the edge its <c>spheres.enter_or_leave</c> names: entering when set, leaving
    /// when clear.
    /// </summary>
    public static bool FiresOn(bool enterOrLeave, bool entering) => enterOrLeave == entering;
}
