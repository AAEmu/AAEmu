using AAEmu.Game.Models.Game.Sagas;

namespace AAEmu.Game.Models.Game.Milestones;

/// <summary>
/// Wire-safety rule for the milestone layer's reuse of the GF-W13 chronicle sync family.
/// </summary>
public static class MilestoneSyncRules
{
    /// <summary>
    /// Whether a milestone may carry an <c>SCChronicleInfoUpdate</c> whose <c>type</c> is the
    /// milestone id. The chronicle type space GF-W13 maps onto is saga-group-keyed — the client's
    /// story tab resolves an update by that main key and moves the entry it finds — so a milestone
    /// whose id equals a shipped saga group id would displace that group's story entry. Such ids
    /// are suppressed and logged instead; there is no milestone-specific packet family in the
    /// 10.0.2.13 corpus to carry them safely. Both inputs are content: the milestone id comes from
    /// the content row the change reports, the group set from the shipped saga catalog.
    /// </summary>
    public static bool CanSyncChronicleType(uint milestoneId, SagaQuestCatalog sagaGroups) =>
        !sagaGroups.TryGetGroup(milestoneId, out _);
}
