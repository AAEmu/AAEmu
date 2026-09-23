using AAEmu.Commons.Utils;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Merchant;
using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>Outcome of one buy attempt against a random merchant window.</summary>
public enum RandomShopPurchaseResult
{
    /// <summary>The claim won, the payment callback succeeded and the offer is sold.</summary>
    Purchased,
    /// <summary>No offer sits in that slot of the current window.</summary>
    OfferNotFound,
    /// <summary>The durable sold 0 -&gt; 1 claim was already taken (this window, this character).</summary>
    AlreadySold,
    /// <summary>The claim was taken and released again because the payment callback refused.</summary>
    PaymentFailed
}

/// <summary>Outcome of one manual refresh against a random merchant window.</summary>
public enum RandomShopRefreshResult
{
    Refreshed,
    /// <content>pack ships refresh_use='f' (shipped pack 3): a manual refresh is not allowed.</content>
    RefreshNotAllowed,
    /// <summary>Free or paid counter is at the pack max for this period (or the period moved on).</summary>
    CounterExhausted,
    /// <summary>The paid-refresh charge callback refused (claim spent was released again).</summary>
    PaymentFailed
}

/// <summary>
/// The random merchants: one stock window per character per pack per UTC day, rolled from the
/// <c>merchant_random_*</c> content, sold exactly once per offer, refreshed within content-defined
/// free/paid budgets, and persisted first so a World kill cannot lose a sale or re-sell an offer.
/// Windows are per-character on purpose - the client reads its own refresh counters, so a shared
/// window would let one character's payment re-roll everyone's stock.
/// </summary>
public class RandomMerchantManager : Singleton<RandomMerchantManager>, ILoadable
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly object _windowLock = new();
    private readonly Dictionary<(uint CharacterId, uint PackId), RandomShopWindow> _windows = [];
    private readonly Random _rng = new();

    private IRandomShopStateStore _store = new InMemoryRandomShopStore();
    private Func<uint, RandomMerchantPack> _packLookup = StaticPackLookup;

    /// <summary>Reads the persisted windows from MySQL. Tests swap the store with UseStore.</summary>
    public void Load()
    {
        lock (_windowLock)
        {
            _store = new MySqlRandomShopStore();
            LoadFromStoreNoLock();
        }
    }

    internal void UseStore(IRandomShopStateStore store)
    {
        lock (_windowLock)
            _store = store ?? new InMemoryRandomShopStore();
    }

    internal void UseContent(IReadOnlyDictionary<uint, RandomMerchantPack> packs)
    {
        lock (_windowLock)
        {
            _packLookup = packs == null
                ? StaticPackLookup
                : packId => packs.TryGetValue(packId, out var pack) ? pack : null;
        }
    }

    /// <summary>Re-hydrates the window cache from the store (startup, and the restart tests).</summary>
    internal void LoadFromStore()
    {
        lock (_windowLock)
            LoadFromStoreNoLock();
    }

    private void LoadFromStoreNoLock()
    {
        _windows.Clear();
        foreach (var window in _store.LoadAll())
            _windows[(window.CharacterId, window.PackId)] = window;
        Logger.Info("Random shop: loaded {0} persisted window(s)", _windows.Count);
    }

    /// <summary>The pack behind an id, or null when no such content row exists (no throw).</summary>
    public RandomMerchantPack TryGetPack(uint packId) => _packLookup(packId);

    /// <summary>
    /// The character's window for <paramref name="packId"/>, rolled for the UTC day containing
    /// <paramref name="nowUtc"/>. A window from an earlier period is re-rolled (counters reset)
    /// and written before it is handed out - persist first, expose after.
    /// </summary>
    public RandomShopWindow GetWindow(uint characterId, uint packId, DateTime nowUtc)
    {
        var pack = RequirePack(packId);
        var now = ServerCalendar.AsUtc(nowUtc);
        var period = ServerCalendar.DailyPeriodStartUtc(now);

        lock (_windowLock)
        {
            if (_windows.TryGetValue((characterId, packId), out var existing) && existing.PeriodStart == period)
                return existing;

            var window = new RandomShopWindow
            {
                CharacterId = characterId,
                PackId = packId,
                PeriodStart = period,
                RolledAt = ToStoredRolledAt(now),
                FreeUsed = 0,
                ChargeUsed = 0,
                Offers = RollOffers(pack)
            };

            if (!_store.SaveWindow(window))
                throw new InvalidOperationException(
                    $"random shop: refusing to expose pack {packId} for character {characterId}: the window state write failed");

            _windows[(characterId, packId)] = window;
            return window;
        }
    }

    /// <summary>
    /// Spends one refresh allowance from the pack's content budget and re-rolls the window.
    /// The spend is the durable conditional update; a failed payment or a failed roll gives the
    /// allowance back (ReleaseRefresh) instead of leaking it.
    /// </summary>
    public RandomShopRefreshResult TryRefresh(
        uint characterId, uint packId, bool isFree, DateTime nowUtc, Func<bool> chargePayment = null,
        Action refundCharge = null)
    {
        var pack = RequirePack(packId);
        if (!pack.RefreshUse)
            return RandomShopRefreshResult.RefreshNotAllowed;

        if (!isFree && pack.RefreshCurrency == null)
            throw new RandomMerchantContentException(
                $"random shop: pack {packId} paid refresh has refresh_currency_id {pack.RefreshCurrencyId}, which is no known content currency");

        var window = GetWindow(characterId, packId, nowUtc);

        lock (_windowLock)
        {
            var period = window.PeriodStart;
            var max = isFree ? pack.RefreshFreeCnt : pack.RefreshChargeCnt;
            if (!_store.TrySpendRefresh(characterId, packId, period, isFree, max))
                return RandomShopRefreshResult.CounterExhausted;

            if (!isFree && !(chargePayment?.Invoke() ?? false))
            {
                _store.ReleaseRefresh(characterId, packId, period, isFree);
                return RandomShopRefreshResult.PaymentFailed;
            }

            var previousOffers = window.Offers;
            var previousRolledAt = window.RolledAt;
            var previousFree = window.FreeUsed;
            var previousCharge = window.ChargeUsed;
            if (isFree)
                window.FreeUsed++;
            else
                window.ChargeUsed++;

            try
            {
                window.Offers = RollOffers(pack);
                window.RolledAt = ToStoredRolledAt(nowUtc);
                if (!_store.SaveWindow(window))
                    throw new InvalidOperationException(
                        $"random shop: refusing to keep character {characterId} pack {packId} refresh: the state write failed");
            }
            catch
            {
                window.Offers = previousOffers;
                window.RolledAt = previousRolledAt;
                window.FreeUsed = previousFree;
                window.ChargeUsed = previousCharge;
                _store.ReleaseRefresh(characterId, packId, period, isFree);
                if (!isFree)
                    refundCharge?.Invoke();
                throw;
            }

            return RandomShopRefreshResult.Refreshed;
        }
    }

    /// <summary>
    /// Sells one offer slot exactly once. The claim is the good the buyer saw (good id and the
    /// window's rolled-at), taken under the window lock so a refresh cannot replace the row
    /// between the lookup and the write. Payment runs after the claim; a refused payment releases it.
    /// </summary>
    public RandomShopPurchaseResult TryPurchase(
        uint characterId, uint packId, int slot, DateTime nowUtc, Func<bool> chargePayment) =>
        TryPurchase(characterId, packId, slot, nowUtc, _ => chargePayment?.Invoke() ?? false);

    public RandomShopPurchaseResult TryPurchase(
        uint characterId, uint packId, int slot, DateTime nowUtc, Func<RandomShopOffer, bool> chargePayment)
    {
        RequirePack(packId);
        GetWindow(characterId, packId, nowUtc);

        RandomShopOffer offer;
        lock (_windowLock)
        {
            if (!_windows.TryGetValue((characterId, packId), out var window))
                return RandomShopPurchaseResult.OfferNotFound;

            offer = window.Offers.FirstOrDefault(candidate => candidate.Slot == slot);
            if (offer == null)
                return RandomShopPurchaseResult.OfferNotFound;
            if (offer.Sold)
                return RandomShopPurchaseResult.AlreadySold;

            if (!_store.TryClaimOffer(characterId, packId, slot, offer.GoodId, window.RolledAt))
                return RandomShopPurchaseResult.AlreadySold;
        }

        if (!(chargePayment?.Invoke(offer) ?? false))
        {
            _store.ReleaseOffer(characterId, packId, slot);
            lock (_windowLock)
                offer.Sold = false;
            return RandomShopPurchaseResult.PaymentFailed;
        }

        lock (_windowLock)
            offer.Sold = true;

        return RandomShopPurchaseResult.Purchased;
    }

    /// <summary>
    /// <c>rolled_at</c> is a whole-second DATETIME. Sub-second ticks never match the stored value.
    /// </summary>
    internal static DateTime ToStoredRolledAt(DateTime value)
    {
        var utc = ServerCalendar.AsUtc(value);
        return new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    }

    private static RandomMerchantPack StaticPackLookup(uint packId) =>
        RandomMerchantGameData.Instance.GetPack(packId);

    public RandomMerchantPack FindPack(uint packId) => _packLookup(packId);

    private RandomMerchantPack RequirePack(uint packId)
    {
        var pack = _packLookup(packId);
        if (pack == null)
        {
            Logger.Error("Random shop: merchant_random_packs row {0} is missing (or not loaded) - refusing", packId);
            throw new RandomMerchantContentException($"merchant_random_packs row {packId} is missing");
        }

        if (!pack.Usable || pack.Currency == null)
        {
            Logger.Error("Random shop: pack {0} was refused by content validation - refusing to open a window", packId);
            throw new RandomMerchantContentException($"merchant_random_packs row {packId} was refused by content validation");
        }

        return pack;
    }

    /// <summary>
    /// sale_cnt distinct groups without replacement (sale_cnt equals the group count for the
    /// all-1-weights packs), then one weighted good inside each picked group. Slot order follows
    /// group_no because EligibleGroups is ordered by it.
    /// </summary>
    private List<RandomShopOffer> RollOffers(RandomMerchantPack pack)
    {
        var groups = pack.EligibleGroups;
        var groupWeights = new long[groups.Count];
        for (var i = 0; i < groups.Count; i++)
            groupWeights[i] = groups[i].Weight;

        var picks = WeightedSelection.PickDistinctIndexes(groupWeights, pack.SaleCnt, _rng);
        var offers = new List<RandomShopOffer>(picks.Count);
        var slot = 0;

        foreach (var pick in picks)
        {
            var group = groups[pick];
            var goodWeights = new long[group.Goods.Count];
            for (var i = 0; i < group.Goods.Count; i++)
                goodWeights[i] = group.Goods[i].Weight;

            var goodIndex = WeightedSelection.PickIndex(goodWeights, _rng);
            if (goodIndex < 0)
            {
                Logger.Error(
                    "Random shop: group {0} of pack {1} yielded no good - the window ships fewer offers than sale_cnt {2}",
                    group.Id, pack.Id, pack.SaleCnt);
                continue;
            }

            var good = group.Goods[goodIndex];
            offers.Add(new RandomShopOffer
            {
                GroupId = group.Id,
                GoodId = good.Id,
                Slot = slot++,
                ItemId = good.ItemId,
                Grade = good.Grade,
                Cost = good.Cost,
                Currency = pack.Currency.Value,
                Sold = false
            });
        }

        return offers;
    }
}
