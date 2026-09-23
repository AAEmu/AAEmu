using AAEmu.Game.Models.Game.Merchant;

namespace AAEmu.Game.Models.Game.Merchant;

/// <summary>Outcome of one reopen-box roll request.</summary>
public enum ReopenRefreshResult
{
    Refreshed,
    /// <summary>The pack ships fewer paid opens than requested (or the free budget is spent).</summary>
    CounterExhausted,
    /// <summary>life_time minutes from the first open have elapsed, so the box is closed.</summary>
    Expired,
    /// <summary>The reward was already taken. A refresh does not open a new claim.</summary>
    AlreadySettled,
    /// <summary>The paid-open charge callback refused.</summary>
    PaymentFailed,
    /// <summary>The content yielded no draw - the roll was released, nothing was spent.</summary>
    NoContent
}

/// <summary>Outcome of one reopen-box reward claim.</summary>
public enum ReopenClaimResult
{
    Claimed,
    /// <summary>No roll sits in this box state (claim before the first refresh).</summary>
    NotRolled,
    /// <summary>The durable settled 0 -&gt; 1 claim was already taken (this box, this character).</summary>
    AlreadySettled,
    /// <summary>The claim was taken and released again because the grant callback refused.</summary>
    GrantFailed
}

/// <summary>
/// One character's state for one reopen-box item instance: the open counters spent against the
/// pack's content budgets, the current roll and its exactly-once claim flag. Keyed by the box
/// item's wire id, because the client's requests address the box instance, not the pack.
/// </summary>
public class ReopenBoxState
{
    public uint CharacterId { get; init; }

    /// <summary>The box item instance id from the wire (u64).</summary>
    public long ItemId { get; init; }

    /// <summary><c>merchant_reopen_packs.id</c> the box was first opened against.</summary>
    public uint PackId { get; set; }

    /// <summary>Free opens spent against pack free_count.</summary>
    public int FreeUsed { get; set; }

    /// <summary>Paid opens spent against pack charge_count.</summary>
    public int ChargeUsed { get; set; }

    /// <summary>When the current roll was made (the record struct's openDate/refreshDate base).</summary>
    public DateTime RolledAt { get; set; }

    /// <summary>When the open box closes: first open + pack life_time minutes. MaxValue means the pack has no lifetime.</summary>
    public DateTime RefreshAvailableAt { get; set; }

    /// <summary>When the first reward of this box state was claimed (record openDate).</summary>
    public DateTime? OpenedAt { get; set; }

    /// <summary>The rolled tier, 0 while no roll exists.</summary>
    public uint GroupId { get; set; }

    /// <summary>The rolled good, 0 while no roll exists.</summary>
    public uint GoodId { get; set; }

    public uint RewardItemId { get; set; }
    public byte RewardGrade { get; set; }
    public int RewardCount { get; set; }

    /// <summary>True once the current roll's reward has been granted - the exactly-once flag.</summary>
    public bool Settled { get; set; }

    /// <summary>A roll is on the table when a good was drawn.</summary>
    public bool HasRoll => GoodId != 0;
}

/// <summary>
/// Durable side of the reopen boxes: state upserts, conditional counter spends and the
/// conditional settled 0 -&gt; 1 claim, same persist-first shape as the random shop store so a
/// World kill can neither lose a reward nor hand one out twice.
/// </summary>
public interface IReopenBoxStateStore
{
    IReadOnlyList<ReopenBoxState> LoadAll();

    /// <summary>Inserts or updates one box state, atomically.</summary>
    bool Save(ReopenBoxState state);

    /// <summary>
    /// Atomically spends one open allowance (free_used/charge_used +1 below the pack max for this
    /// box). Returns false when the counter is exhausted.
    /// </summary>
    bool TrySpendOpen(uint characterId, long itemId, bool isCharge, int max);

    /// <summary>Gives back an open allowance whose payment or roll failed afterwards.</summary>
    bool ReleaseOpen(uint characterId, long itemId, bool isCharge);

    /// <summary>Atomically claims the current roll (settled 0 -&gt; 1). False when already settled.</summary>
    bool TrySettle(uint characterId, long itemId);

    /// <summary>Releases a claim taken before the grant failed.</summary>
    bool ReleaseSettle(uint characterId, long itemId);

    /// <summary>Drops the row so a remaining stack of the same item id can be opened again.</summary>
    bool Forget(uint characterId, long itemId);
}
