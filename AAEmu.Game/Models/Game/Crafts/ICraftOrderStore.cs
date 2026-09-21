namespace AAEmu.Game.Models.Game.Crafts;

/// <summary>The live board rows. Writes happen at post / cancel / fill / expiry, not on the 5-minute save tick.</summary>
public interface ICraftOrderStore
{
    IReadOnlyList<CraftOrder> LoadAll();
    bool Insert(CraftOrder order);
    bool Delete(ulong orderId);
    bool DeleteAll();

    IReadOnlyList<CraftOrderFeeStat> LoadFeeStats();
    bool UpsertFeeStats(CraftOrderFeeStat stat);
}
