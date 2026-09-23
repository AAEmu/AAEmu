using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Merchant;

/// <summary>
/// One character's current window over one random merchant pack: the offers rolled for the
/// current UTC day plus the refresh counters spent on it.
/// </summary>
/// <remarks>
/// Per-character rather than world stock: the client reads its own refresh counters
/// (X2Store GetRandomShopStoreRefreshCount returns freeCnt/freeMax, chargeCnt/chargeMax) and
/// its paid refresh checks the viewer's own inventory (hasInven), so a shared window would let
/// one character's payment re-roll everyone's stock. The wire struct agrees - shopDisplayInfo
/// carries freeCnt/chargeCnt/dbid/recordTime together, i.e. counters and
/// record identity travel as one per-viewer payload. State lives in character_* tables.
/// </remarks>
public class RandomShopWindow
{
    public uint CharacterId { get; init; }
    public uint PackId { get; init; }

    /// <summary>UTC midnight of the day this window was rolled for (ServerCalendar daily period).</summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>When the current offers were rolled - maps to shopDisplayInfo.recordTime for E15.</summary>
    public DateTime RolledAt { get; set; }

    /// <summary>Free refreshes spent this period (pack.RefreshFreeCnt is the max).</summary>
    public int FreeUsed { get; set; }

    /// <summary>Paid refreshes spent this period (pack.RefreshChargeCnt is the max).</summary>
    public int ChargeUsed { get; set; }

    /// <summary>The rolled offers, ordered by group_no; slot indexes match the wire display order field.</summary>
    public List<RandomShopOffer> Offers { get; set; } = [];
}

/// <summary>One offer slot inside a <see cref="RandomShopWindow"/>; sold exactly once per window.</summary>
public class RandomShopOffer
{
    public uint GroupId { get; init; }

    /// <summary><c>merchant_random_goods.id</c> of the drawn row.</summary>
    public uint GoodId { get; init; }

    /// <summary>Display slot; the wire display element's order field.</summary>
    public int Slot { get; init; }

    public uint ItemId { get; init; }
    public byte Grade { get; init; }

    /// <summary>Price snapshot taken at roll time from merchant_random_goods.cost - persisted so the quote survives restarts.</summary>
    public int Cost { get; init; }

    public ShopCurrencyType Currency { get; init; }

    /// <summary>
    /// True once the offer has been sold. One unit per offer: merchant_random_goods carries no
    /// count column (merchant_reopen_goods does), the store disables the quantity spinner for
    /// random shops (store.lua), and the client flags offers soldout per slot.
    /// </summary>
    public bool Sold { get; set; }

    /// <summary>Remaining units of this offer in the current window (0 or 1).</summary>
    public int Remaining => Sold ? 0 : 1;
}
