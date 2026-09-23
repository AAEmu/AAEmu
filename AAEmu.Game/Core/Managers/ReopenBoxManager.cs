using AAEmu.Commons.Utils;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Merchant;
using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// The reopen boxes: one state per character per box item instance, rolled through the pack's
/// two-stage weighted draw, bounded by the pack's content open budgets and life_time cooldown,
/// and settled exactly once per roll (persist-first conditional claim, same idiom as the random
/// shop). Every gameplay number - limits, prices, cooldown minutes, weights - comes from
/// <c>merchant_reopen_*</c> content; a missing or refused pack fails loudly.
/// </summary>
public class ReopenBoxManager : Singleton<ReopenBoxManager>, ILoadable
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly object _stateLock = new();
    private readonly Dictionary<(uint CharacterId, long ItemId), ReopenBoxState> _states = [];

    private IReopenBoxStateStore _store = new InMemoryReopenBoxStore();
    private Func<uint, MerchantReopenPack> _packLookup = packId =>
        MerchantReopenPackGameData.Instance.GetPack(packId);

    /// <summary>Reads the persisted box states from MySQL. Tests swap the store with UseStore.</summary>
    public void Load()
    {
        lock (_stateLock)
        {
            _store = new MySqlReopenBoxStore();
            LoadFromStoreNoLock();
        }
    }

    internal void UseStore(IReopenBoxStateStore store)
    {
        lock (_stateLock)
            _store = store ?? new InMemoryReopenBoxStore();
    }

    internal void UseContent(IReadOnlyDictionary<uint, MerchantReopenPack> packs)
    {
        lock (_stateLock)
        {
            _packLookup = packs == null
                ? packId => MerchantReopenPackGameData.Instance.GetPack(packId)
                : packId => packs.TryGetValue(packId, out var pack) ? pack : null;
        }
    }

    /// <summary>Re-hydrates the state cache from the store (startup, and the restart tests).</summary>
    internal void LoadFromStore()
    {
        lock (_stateLock)
            LoadFromStoreNoLock();
    }

    private void LoadFromStoreNoLock()
    {
        _states.Clear();
        foreach (var state in _store.LoadAll())
            _states[(state.CharacterId, state.ItemId)] = state;
        Logger.Info("Reopen box: loaded {0} persisted box state(s)", _states.Count);
    }

    /// <summary>The pack behind an id, or null when no such content row exists (no throw).</summary>
    public MerchantReopenPack TryGetPack(uint packId) => _packLookup(packId);

    /// <summary>The character's state for one box instance, or null when it has never been opened.</summary>
    public ReopenBoxState TryGetState(uint characterId, long itemId)
    {
        lock (_stateLock)
            return _states.TryGetValue((characterId, itemId), out var state) ? state : null;
    }

    /// <summary>
    /// Spends one open allowance from the pack's content budget (free or paid), enforces the
    /// life_time cooldown, re-rolls the box through the two-stage weighted draw and persists the
    /// state before handing it out. The durable counter spend is released again whenever the
    /// charge or the roll fails, so nothing leaks.
    /// </summary>
    public ReopenRefreshResult TryRefresh(
        uint characterId, long itemId, uint packId, bool isCharge, DateTime nowUtc, Func<bool> chargePayment = null,
        Action refundCharge = null)
    {
        var pack = RequirePack(packId);
        var now = ServerCalendar.AsUtc(nowUtc);

        lock (_stateLock)
        {
            var state = GetOrCreateNoLock(characterId, itemId, packId, now);
            if (state.PackId != packId)
                throw new RandomMerchantContentException(
                    $"reopen box: item {itemId} was opened against pack {state.PackId}, not {packId} - refusing");

            if (state.Settled)
                return ReopenRefreshResult.AlreadySettled;

            if (pack.LifeTime > 0 && now >= state.RefreshAvailableAt)
                return ReopenRefreshResult.Expired;

            var max = isCharge ? pack.ChargeCount : pack.FreeCount;
            if (!_store.TrySpendOpen(characterId, itemId, isCharge, max))
                return ReopenRefreshResult.CounterExhausted;

            if (isCharge && pack.ChargePoint > 0 && !(chargePayment?.Invoke() ?? false))
            {
                _store.ReleaseOpen(characterId, itemId, isCharge);
                return ReopenRefreshResult.PaymentFailed;
            }

            var good = MerchantReopenPackGameData.Roll(pack, Random.Shared);
            if (good == null)
            {
                _store.ReleaseOpen(characterId, itemId, isCharge);
                Logger.Error(
                    "Reopen box: pack {0} yielded no draw for character {1} item {2} - the open was released",
                    packId, characterId, itemId);
                return ReopenRefreshResult.NoContent;
            }

            var group = FindGroup(pack, good);
            var previous = Snapshot(state);

            if (isCharge)
                state.ChargeUsed++;
            else
                state.FreeUsed++;
            state.PackId = packId;
            state.RolledAt = now;
            state.GroupId = group?.Id ?? 0;
            state.GoodId = good.Id;
            state.RewardItemId = good.ItemId;
            state.RewardGrade = good.GradeId;
            state.RewardCount = good.Count;

            if (!_store.Save(state))
            {
                Restore(state, previous);
                _store.ReleaseOpen(characterId, itemId, isCharge);
                refundCharge?.Invoke();
                throw new InvalidOperationException(
                    $"reopen box: refusing to keep character {characterId} item {itemId} roll: the state write failed");
            }

            return ReopenRefreshResult.Refreshed;
        }
    }

    /// <summary>
    /// Grants the current roll exactly once. The durable settled 0 -&gt; 1 claim is the
    /// concurrency gate and runs outside any application step: two racing claims both reach it,
    /// exactly one wins, and only the winner's grant callback runs; a refused grant releases the
    /// claim again instead of stranding the roll.
    /// </summary>
    public ReopenClaimResult TryClaim(
        uint characterId, long itemId, DateTime nowUtc, Func<ReopenBoxState, bool> grant)
    {
        ReopenBoxState state;
        lock (_stateLock)
        {
            if (!_states.TryGetValue((characterId, itemId), out state))
                return ReopenClaimResult.NotRolled;
            if (!state.HasRoll)
                return ReopenClaimResult.NotRolled;
            if (state.Settled)
                return ReopenClaimResult.AlreadySettled;
        }

        if (!_store.TrySettle(characterId, itemId))
        {
            // Another claim (this process or another) holds it: mirror the durable state.
            lock (_stateLock)
                state.Settled = true;
            return ReopenClaimResult.AlreadySettled;
        }

        var granted = false;
        try
        {
            granted = grant?.Invoke(state) ?? false;
        }
        catch
        {
            _store.ReleaseSettle(characterId, itemId);
            lock (_stateLock)
                state.Settled = false;
            throw;
        }

        if (!granted)
        {
            _store.ReleaseSettle(characterId, itemId);
            lock (_stateLock)
                state.Settled = false;
            return ReopenClaimResult.GrantFailed;
        }

        lock (_stateLock)
        {
            state.Settled = true;
            if (!state.OpenedAt.HasValue)
                state.OpenedAt = ServerCalendar.AsUtc(nowUtc);
            if (!_store.Save(state))
                Logger.Error(
                    "Reopen box: character {0} claimed item {1}, but the state rewrite failed (the claim row already holds settled = 1)",
                    characterId, itemId);
        }

        return ReopenClaimResult.Claimed;
    }

    private ReopenBoxState GetOrCreateNoLock(uint characterId, long itemId, uint packId, DateTime now)
    {
        if (_states.TryGetValue((characterId, itemId), out var state))
            return state;

        var openedPack = _packLookup(packId);
        var lifeMinutes = openedPack?.LifeTime ?? 0;
        state = new ReopenBoxState
        {
            CharacterId = characterId,
            ItemId = itemId,
            PackId = packId,
            RolledAt = now,
            RefreshAvailableAt = lifeMinutes > 0 ? now.AddMinutes(lifeMinutes) : DateTime.MaxValue
        };
        if (!_store.Save(state))
            throw new InvalidOperationException(
                $"reopen box: refusing to expose item {itemId} for character {characterId}: the state write failed");
        _states[(characterId, itemId)] = state;
        return state;
    }

    private MerchantReopenPack RequirePack(uint packId)
    {
        var pack = _packLookup(packId);
        if (pack == null)
        {
            Logger.Error("Reopen box: merchant_reopen_packs row {0} is missing (or not loaded) - refusing", packId);
            throw new RandomMerchantContentException($"merchant_reopen_packs row {packId} is missing");
        }

        if (!pack.Usable)
        {
            Logger.Error("Reopen box: pack {0} was refused by content validation - refusing to open it", packId);
            throw new RandomMerchantContentException($"merchant_reopen_packs row {packId} was refused by content validation");
        }

        return pack;
    }

    private static MerchantReopenGroup FindGroup(MerchantReopenPack pack, MerchantReopenGood good) =>
        pack.Groups.FirstOrDefault(group => group.Goods.Contains(good));

    private static ReopenBoxStateSnapshot Snapshot(ReopenBoxState state) => new(
        state.FreeUsed, state.ChargeUsed, state.RolledAt, state.RefreshAvailableAt,
        state.GroupId, state.GoodId, state.RewardItemId, state.RewardGrade, state.RewardCount, state.Settled);

    private static void Restore(ReopenBoxState state, ReopenBoxStateSnapshot previous)
    {
        state.FreeUsed = previous.FreeUsed;
        state.ChargeUsed = previous.ChargeUsed;
        state.RolledAt = previous.RolledAt;
        state.RefreshAvailableAt = previous.RefreshAvailableAt;
        state.GroupId = previous.GroupId;
        state.GoodId = previous.GoodId;
        state.RewardItemId = previous.RewardItemId;
        state.RewardGrade = previous.RewardGrade;
        state.RewardCount = previous.RewardCount;
        state.Settled = previous.Settled;
    }

    private readonly record struct ReopenBoxStateSnapshot(
        int FreeUsed, int ChargeUsed, DateTime RolledAt, DateTime RefreshAvailableAt,
        uint GroupId, uint GoodId, uint RewardItemId, byte RewardGrade, int RewardCount, bool Settled);
}
