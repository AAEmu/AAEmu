namespace AAEmu.Game.Models.Game.Merchant;

/// <summary>
/// Process-lifetime store used by tests, and by a World that has not loaded MySQL yet. The
/// conditional updates mirror the MySQL store exactly: a claim is the only way an offer flips to
/// sold, and it flips once no matter how many callers race for the slot.
/// </summary>
public sealed class InMemoryRandomShopStore : IRandomShopStateStore
{
    private readonly object _lock = new();
    private readonly Dictionary<(uint CharacterId, uint PackId), RandomShopWindow> _windows = [];

    public IReadOnlyList<RandomShopWindow> LoadAll()
    {
        lock (_lock)
            return _windows.Values.Select(Clone).ToList();
    }

    public bool SaveWindow(RandomShopWindow window)
    {
        if (window == null)
            return false;
        lock (_lock)
            _windows[(window.CharacterId, window.PackId)] = Clone(window);
        return true;
    }

    public bool TryClaimOffer(uint characterId, uint packId, int slot)
    {
        lock (_lock)
        {
            if (!_windows.TryGetValue((characterId, packId), out var window))
                return false;
            var offer = window.Offers.FirstOrDefault(candidate => candidate.Slot == slot);
            if (offer == null || offer.Sold)
                return false;
            offer.Sold = true;
            return true;
        }
    }

    public bool ReleaseOffer(uint characterId, uint packId, int slot)
    {
        lock (_lock)
        {
            if (!_windows.TryGetValue((characterId, packId), out var window))
                return false;
            var offer = window.Offers.FirstOrDefault(candidate => candidate.Slot == slot);
            if (offer == null || !offer.Sold)
                return false;
            offer.Sold = false;
            return true;
        }
    }

    public bool TrySpendRefresh(uint characterId, uint packId, DateTime periodStart, bool isFree, int max)
    {
        lock (_lock)
        {
            if (!_windows.TryGetValue((characterId, packId), out var window) || window.PeriodStart != periodStart)
                return false;
            var used = isFree ? window.FreeUsed : window.ChargeUsed;
            if (used >= max)
                return false;
            if (isFree)
                window.FreeUsed = used + 1;
            else
                window.ChargeUsed = used + 1;
            return true;
        }
    }

    public bool ReleaseRefresh(uint characterId, uint packId, DateTime periodStart, bool isFree)
    {
        lock (_lock)
        {
            if (!_windows.TryGetValue((characterId, packId), out var window) || window.PeriodStart != periodStart)
                return false;
            if (isFree)
            {
                if (window.FreeUsed <= 0)
                    return false;
                window.FreeUsed--;
            }
            else
            {
                if (window.ChargeUsed <= 0)
                    return false;
                window.ChargeUsed--;
            }
            return true;
        }
    }

    /// <summary>Deep copy so caller mutations never alias the durable rows.</summary>
    private static RandomShopWindow Clone(RandomShopWindow window) => new()
    {
        CharacterId = window.CharacterId,
        PackId = window.PackId,
        PeriodStart = window.PeriodStart,
        RolledAt = window.RolledAt,
        FreeUsed = window.FreeUsed,
        ChargeUsed = window.ChargeUsed,
        Offers = window.Offers
            .Select(offer => new RandomShopOffer
            {
                GroupId = offer.GroupId,
                GoodId = offer.GoodId,
                Slot = offer.Slot,
                ItemId = offer.ItemId,
                Grade = offer.Grade,
                Cost = offer.Cost,
                Currency = offer.Currency,
                Sold = offer.Sold
            })
            .ToList()
    };
}
