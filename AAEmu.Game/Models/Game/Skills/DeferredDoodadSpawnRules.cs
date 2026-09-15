namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Timing and lifecycle decision for the SpawnDoodad special effect. The doodad is created, placed and
/// initialised the moment the effect runs; only making it visible waits for the effect's delay, which
/// used to be slept off on the effect thread.
/// </summary>
public static class DeferredDoodadSpawnRules
{
    /// <summary>
    /// A positive delay defers the spawn to the scheduler. Zero and negative delays have nothing to
    /// wait for (content uses both: 206 of the 382 spawn_doodad rows carry value2 &lt;= 0, down to
    /// -5000), so the doodad is spawned inline instead of queueing a task for the same tick.
    /// </summary>
    public static bool ShouldDeferSpawn(int delayMilliseconds) => delayMilliseconds > 0;

    /// <summary>
    /// Re-checked when the deferred task runs: inside the delay the doodad can be deleted again or its
    /// world can be torn down, and <c>Doodad.Spawn</c> throws for a doodad with no parent world.
    /// </summary>
    /// <remarks>
    /// A torn-down world is the case a parent-world check cannot see: <c>WorldInstance.Dispose</c> tears the
    /// instance down and leaves every object's <c>ParentWorld</c> pointing at it, so the delayed spawn would
    /// add and show its doodad in a world that is gone. That is what
    /// <see cref="World.WorldInstance.IsDisposed"/> is for, and why the flag is public.
    /// </remarks>
    public static bool CanSpawn(bool doodadDeleted, bool hasParentWorld, bool worldDisposed) =>
        !doodadDeleted && hasParentWorld && !worldDisposed;
}
