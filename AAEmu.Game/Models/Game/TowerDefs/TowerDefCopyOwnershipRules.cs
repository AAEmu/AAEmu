namespace AAEmu.Game.Models.Game.TowerDefs;

/// <summary>
/// Whether a unit, kill, or host belongs to one tower-def run — a world event or one instance copy.
/// </summary>
/// <remarks>
/// Several copies of the same zone key can run the same <c>tower_defs</c> row at once. Their
/// units share that zone key and that template id, so neither is enough to name the run. The
/// copy's world-instance id is: zero is the overworld (world events and their units), and a
/// non-zero id is exactly one copy.
/// </remarks>
public static class TowerDefCopyOwnershipRules
{
    /// <summary>
    /// True when <paramref name="unitInstanceId"/> is in the same copy as
    /// <paramref name="ownerInstanceId"/>.
    /// </summary>
    /// <param name="ownerInstanceId">The run's copy id; zero for a world event.</param>
    /// <param name="unitInstanceId">The unit's (or kill's) world-instance id.</param>
    public static bool SameCopy(uint ownerInstanceId, uint unitInstanceId)
        => ownerInstanceId == unitInstanceId;

    /// <summary>
    /// True when a disconnected zone host owns this instance run.
    /// </summary>
    /// <remarks>
    /// An overworld host (instance id zero) never owns a copy's run. Sibling copies share a
    /// zone key, so the host is identified by its copy id alone.
    /// </remarks>
    public static bool InstanceRunOwnedByHost(uint runInstanceId, uint hostInstanceId)
        => runInstanceId != 0 && runInstanceId == hostInstanceId;
}
