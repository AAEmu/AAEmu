namespace AAEmu.Game.Models.Game.Merchant;

/// <summary>
/// Process-lifetime store used by tests, and by a World that has not loaded MySQL yet. The
/// conditional updates mirror the MySQL store exactly: settled is the only way a roll flips to
/// claimed, and it flips once no matter how many callers race for it.
/// </summary>
public sealed class InMemoryReopenBoxStore : IReopenBoxStateStore
{
    private readonly object _lock = new();
    private readonly Dictionary<(uint CharacterId, long ItemId), ReopenBoxState> _states = [];

    public IReadOnlyList<ReopenBoxState> LoadAll()
    {
        lock (_lock)
            return _states.Values.Select(Clone).ToList();
    }

    public bool Save(ReopenBoxState state)
    {
        if (state == null)
            return false;
        lock (_lock)
            _states[(state.CharacterId, state.ItemId)] = Clone(state);
        return true;
    }

    public bool TrySpendOpen(uint characterId, long itemId, bool isCharge, int max)
    {
        lock (_lock)
        {
            if (!_states.TryGetValue((characterId, itemId), out var state))
                return false;
            var used = isCharge ? state.ChargeUsed : state.FreeUsed;
            if (used >= max)
                return false;
            if (isCharge)
                state.ChargeUsed = used + 1;
            else
                state.FreeUsed = used + 1;
            return true;
        }
    }

    public bool ReleaseOpen(uint characterId, long itemId, bool isCharge)
    {
        lock (_lock)
        {
            if (!_states.TryGetValue((characterId, itemId), out var state))
                return false;
            if (isCharge)
            {
                if (state.ChargeUsed <= 0)
                    return false;
                state.ChargeUsed--;
            }
            else
            {
                if (state.FreeUsed <= 0)
                    return false;
                state.FreeUsed--;
            }
            return true;
        }
    }

    public bool TrySettle(uint characterId, long itemId)
    {
        lock (_lock)
        {
            if (!_states.TryGetValue((characterId, itemId), out var state) || state.Settled)
                return false;
            state.Settled = true;
            return true;
        }
    }

    public bool ReleaseSettle(uint characterId, long itemId)
    {
        lock (_lock)
        {
            if (!_states.TryGetValue((characterId, itemId), out var state) || !state.Settled)
                return false;
            state.Settled = false;
            return true;
        }
    }

    public bool Forget(uint characterId, long itemId)
    {
        lock (_lock)
            return _states.Remove((characterId, itemId));
    }

    /// <summary>Deep copy so caller mutations never alias the durable rows.</summary>
    private static ReopenBoxState Clone(ReopenBoxState state) => new()
    {
        CharacterId = state.CharacterId,
        ItemId = state.ItemId,
        PackId = state.PackId,
        FreeUsed = state.FreeUsed,
        ChargeUsed = state.ChargeUsed,
        RolledAt = state.RolledAt,
        RefreshAvailableAt = state.RefreshAvailableAt,
        OpenedAt = state.OpenedAt,
        GroupId = state.GroupId,
        GoodId = state.GoodId,
        RewardItemId = state.RewardItemId,
        RewardGrade = state.RewardGrade,
        RewardCount = state.RewardCount,
        Settled = state.Settled
    };
}
