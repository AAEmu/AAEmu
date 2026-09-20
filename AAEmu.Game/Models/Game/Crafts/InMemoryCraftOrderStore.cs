namespace AAEmu.Game.Models.Game.Crafts;

/// <summary>Process-lifetime store used by tests, and by a World that has not loaded MySQL yet.</summary>
public sealed class InMemoryCraftOrderStore : ICraftOrderStore
{
    private readonly Dictionary<ulong, CraftOrder> _rows = [];
    private readonly Dictionary<uint, CraftOrderFeeStat> _feeStats = [];

    public IReadOnlyList<CraftOrder> LoadAll() => _rows.Values.ToList();

    public bool Insert(CraftOrder order)
    {
        if (order == null || order.Id == 0)
            return false;
        _rows[order.Id] = order;
        return true;
    }

    public bool Delete(ulong orderId) => _rows.Remove(orderId);

    public bool DeleteAll()
    {
        _rows.Clear();
        _feeStats.Clear();
        return true;
    }

    public IReadOnlyList<CraftOrderFeeStat> LoadFeeStats() => _feeStats.Values.ToList();

    public bool UpsertFeeStats(CraftOrderFeeStat stat)
    {
        if (stat.CraftId == 0)
            return false;
        _feeStats[stat.CraftId] = stat;
        return true;
    }
}
