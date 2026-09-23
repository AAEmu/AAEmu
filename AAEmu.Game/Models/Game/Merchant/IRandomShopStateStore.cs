namespace AAEmu.Game.Models.Game.Merchant;

/// <summary>
/// Durable side of the random shop: windows, offers and refresh counters. Writes happen at
/// window roll / refresh / buy (same persist-first shape as the craft order board), so a World
/// kill cannot lose a sale or hand out a sold offer twice.
/// </summary>
public interface IRandomShopStateStore
{
    /// <summary>All persisted windows with their offers, keyed rows assembled by the caller.</summary>
    IReadOnlyList<RandomShopWindow> LoadAll();

    /// <summary>Inserts or updates one window header and replaces its offer rows, atomically.</summary>
    bool SaveWindow(RandomShopWindow window);

    /// <summary>
    /// Atomically claims one offer slot (sold 0 -&gt; 1). Returns false when the slot is already
    /// sold - the conditional update is the exactly-once gate under concurrency.
    /// </summary>
    /// <summary>
    /// Claims the offer only when the row is still that good, rolled at <paramref name="rolledAt"/>.
    /// A refresh that replaced the window makes this return false.
    /// </summary>
    bool TryClaimOffer(uint characterId, uint packId, int slot, uint goodId, DateTime rolledAt);

    /// <summary>Releases a claim taken before payment (the payment failed).</summary>
    bool ReleaseOffer(uint characterId, uint packId, int slot);

    /// <summary>
    /// Atomically spends one refresh allowance (free_used/charge_used +1 below the pack max for
    /// this period). Returns false when the counter is exhausted or the period no longer matches.
    /// </summary>
    bool TrySpendRefresh(uint characterId, uint packId, DateTime periodStart, bool isFree, int max);

    /// <summary>Gives back a refresh allowance whose payment or roll failed afterwards.</summary>
    bool ReleaseRefresh(uint characterId, uint packId, DateTime periodStart, bool isFree);
}
