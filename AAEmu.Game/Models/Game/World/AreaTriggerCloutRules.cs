namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Decision helpers for doodad clout area triggers. Many shipped
/// <c>doodad_func_clouts</c> rows carry <c>buff_id</c> 0/NULL but still list
/// <c>doodad_func_clout_effects</c> (damage, etc.) — those must tick without a dwell buff.
/// </summary>
public static class AreaTriggerCloutRules
{
    /// <summary>
    /// Whether units inside the shape should be tracked even when there is no inside-buff.
    /// </summary>
    public static bool TracksOccupantsWithoutBuff(bool hasInsideBuff, bool hasEffects) =>
        hasInsideBuff || hasEffects;

    /// <summary>
    /// Whether the tick should apply registered effects for the current occupants.
    /// </summary>
    public static bool AppliesEffectsOnTick(bool hasInsideBuff, bool hasEffects) =>
        hasInsideBuff || hasEffects;

    /// <summary>
    /// Whether a clout's lifetime can finish before the area-trigger manager's next tick, so the
    /// arm path must pulse once immediately.
    /// </summary>
    public static bool NeedsImmediatePulse(int durationMs, int managerTickMs) =>
        durationMs > 0 && durationMs <= managerTickMs;

    /// <summary>
    /// A clout armed from the doodad's initial phase runs before <c>Spawn</c> marks the owner
    /// visible and places it in a region. Retiring the trigger on that first pulse deletes the
    /// area, so a later dwell buff never lands. Wait for the owner to become visible instead —
    /// and do not advance the effect clock until then.
    /// </summary>
    public static bool WaitUntilOwnerVisible(bool ownerVisible) => !ownerVisible;

    /// <summary>
    /// When the arm pulse ran before the owner was visible, <c>Doodad.Spawn</c> must pulse again
    /// so a short clout is not left waiting for the manager's 200 ms tick.
    /// </summary>
    public static bool NeedsPostSpawnPulse(bool ownerWasVisibleAtArm) => !ownerWasVisibleAtArm;

}
