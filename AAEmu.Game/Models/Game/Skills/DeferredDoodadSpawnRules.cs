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
    public static bool CanSpawn(bool doodadDeleted, bool hasParentWorld) => !doodadDeleted && hasParentWorld;
}
