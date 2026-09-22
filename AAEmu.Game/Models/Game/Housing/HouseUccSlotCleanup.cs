namespace AAEmu.Game.Models.Game.Housing;

/// <summary>
/// Keeps crest-slot rows from outliving their house. House ids are reused (the lowest free id is handed
/// out again), so a slot row left behind by a removed house would put its crests on the next house that
/// gets that id.
/// </summary>
/// <remarks>
/// A removed house's rows are deleted straight away. When that delete fails, the id stays pending and the
/// next world save deletes the rows, unless a house with that id wrote its own slots in the meantime: that
/// write already replaced every row for the id, and deleting afterwards would take the new house's crests.
/// </remarks>
public sealed class HouseUccSlotCleanup
{
    private readonly object _sync = new();
    private readonly HashSet<uint> _pending = [];

    /// <summary>The rows of this house id still have to be deleted.</summary>
    public void MarkPending(uint houseId)
    {
        lock (_sync)
            _pending.Add(houseId);
    }

    /// <summary>The slots of a live house with this id were written, which replaced every row for the id.</summary>
    public void Written(uint houseId)
    {
        lock (_sync)
            _pending.Remove(houseId);
    }

    /// <summary>The ids whose rows still have to be deleted; they stay pending until <see cref="Deleted"/>.</summary>
    public List<uint> Pending()
    {
        lock (_sync)
            return [.. _pending];
    }

    /// <summary>The rows of these ids were deleted.</summary>
    public void Deleted(IEnumerable<uint> houseIds)
    {
        lock (_sync)
        {
            foreach (var houseId in houseIds)
                _pending.Remove(houseId);
        }
    }

    /// <summary>Slot-row house ids with no loaded house: left behind by a removal and never deleted.</summary>
    public static List<uint> Orphans(IEnumerable<uint> rowHouseIds, Func<uint, bool> houseExists) =>
        rowHouseIds.Distinct().Where(houseId => !houseExists(houseId)).ToList();
}
