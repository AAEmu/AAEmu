using AAEmu.Game.Models.Game.Auction.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Auction;

/// <summary>
/// House constants and matching that do not need a live World.
/// </summary>
public static class AuctionHouseRules
{
    public const int SearchPageSize = 9;
    public const int SoldRecordDays = 14;
    public const int MultilingualItemIdLimit = 130;
    public const byte UnsetWorldId = 255;
    public const long MaxEscrowCopper = int.MaxValue;

    public static int HoursFor(AuctionDuration duration) => duration switch
    {
        AuctionDuration.AuctionDuration6Hours => 6,
        AuctionDuration.AuctionDuration12Hours => 12,
        AuctionDuration.AuctionDuration24Hours => 24,
        AuctionDuration.AuctionDuration48Hours => 48,
        _ => 48
    };

    public static bool IsSoulBound(Item item) =>
        item != null && item.HasFlag(ItemFlag.SoulBound);

    public static bool HasUcc(Item item) =>
        item != null && item.HasFlag(ItemFlag.HasUCC);

    public static bool IsOwnedInBag(Item item, uint playerId) =>
        item != null && item.OwnerId == playerId && item.SlotType == SlotType.Inventory;

    public static bool IsHeldByHouse(Item item) =>
        item != null && item.SlotType == SlotType.Auction && item.Count > 0;

    public static bool IsEscrowSlot(SlotType slot) =>
        slot is SlotType.Auction or SlotType.Mail;

    /// <summary>
    /// True when the player can still move, use, or summon this item. House and mail holdings
    /// stay server-owned until a house settle or a mail take moves them.
    /// </summary>
    public static bool IsPlayerHeldItem(Item item) =>
        item != null && item.Count > 0 && item.SlotType is
            SlotType.Inventory or SlotType.Equipment or SlotType.Bank
            or SlotType.EquipmentMate or SlotType.EquipmentSlave;

    public static bool IsValidDuration(AuctionDuration duration) =>
        duration is AuctionDuration.AuctionDuration6Hours
            or AuctionDuration.AuctionDuration12Hours
            or AuctionDuration.AuctionDuration24Hours
            or AuctionDuration.AuctionDuration48Hours;

    public static bool FitsEscrow(long copper) =>
        copper >= 0 && copper <= MaxEscrowCopper;

    public static bool PricesAreListable(long startPrice, long buyoutPrice) =>
        FitsEscrow(startPrice) && FitsEscrow(buyoutPrice)
        && (startPrice > 0 || buyoutPrice > 0)
        && (buyoutPrice == 0 || startPrice == 0 || buyoutPrice >= startPrice);

    public static bool IsListableItem(Item item)
    {
        if (item?.Template == null || item.Count <= 0)
            return false;
        if (IsSoulBound(item) || HasUcc(item))
            return false;
        if (!item.Template.Sellable)
            return false;
        return item.Template.BindType is not (ItemBindType.BindOnPickup or ItemBindType.BindOnPickupPack);
    }

    public static bool IsBuyoutOffer(long offer, long buyout) =>
        buyout > 0 && offer >= buyout;

    /// <summary>
    /// Copper to take from the bidder now. A raise by the standing bidder is only the difference.
    /// </summary>
    public static bool TryGetBidCharge(
        long offer,
        long startMoney,
        long currentBid,
        long buyout,
        bool bidderIsCurrent,
        out long charge,
        out bool isBuyout)
    {
        charge = 0;
        isBuyout = IsBuyoutOffer(offer, buyout);
        if (offer <= 0 || !FitsEscrow(offer) || !FitsEscrow(startMoney) || !FitsEscrow(currentBid) || !FitsEscrow(buyout))
            return false;

        var target = isBuyout ? buyout : offer;
        if (!isBuyout)
        {
            if (startMoney > 0 && target < startMoney)
                return false;
            if (target <= currentBid)
                return false;
        }
        else if (bidderIsCurrent && currentBid >= buyout)
        {
            return false;
        }

        charge = bidderIsCurrent ? target - currentBid : target;
        return charge > 0;
    }

    public static (int minStack, int maxStack) ClampStacks(int itemCount, int minStack, int maxStack, bool allowPartial)
    {
        var count = Math.Max(1, itemCount);
        if (!allowPartial)
            return (count, count);

        var min = Math.Clamp(minStack, 1, count);
        var max = Math.Clamp(maxStack, min, count);
        return (min, max);
    }

    /// <summary>
    /// How many of a listed stack this bid takes. Partial buy is off unless the
    /// house feature is on; a missing or zero request then takes the full stack.
    /// </summary>
    public static int ResolveBidStack(int requested, int itemCount, int minStack, int maxStack, bool allowPartial)
    {
        var count = Math.Max(1, itemCount);
        var (min, max) = ClampStacks(count, minStack, maxStack, allowPartial);
        if (!allowPartial)
            return count;

        var want = requested <= 0 ? max : requested;
        return Math.Clamp(want, min, max);
    }

    public static int ClampMultilingualCount(int count)
    {
        if (count <= 0)
            return 0;
        return Math.Min(count, MultilingualItemIdLimit);
    }

    /// <summary>
    /// Listing-time commission rate. Item overrides beat the house default; the
    /// account-buff discount is applied once here and stored on the lot.
    /// </summary>
    public static int ListingChargeRate(int saleChargeRate, int itemChargeRate, int discountPercent)
    {
        var rate = itemChargeRate > 0 ? itemChargeRate : saleChargeRate;
        return AuctionFeeSchedule.ApplyPercentDiscount(rate, discountPercent);
    }

    /// <summary>
    /// Settlement uses the stored listing rate when the lot has one. Older rows
    /// with <c>charge_percent = 0</c> fall back to the item / house rate.
    /// </summary>
    public static long SaleChargeForLot(AuctionFeeSchedule fees, long soldAmount, int storedChargePercent, int itemChargeRate)
    {
        if (fees == null)
            return 0;
        if (storedChargePercent > 0)
            return fees.GetSaleCharge(soldAmount, storedChargePercent);
        return fees.GetSaleCharge(soldAmount, itemChargeRate);
    }

    public static void ReturnToHouseEscrow(Item item, ulong sellerId)
    {
        if (item == null)
            return;
        item.OwnerId = sellerId;
        item.SlotType = SlotType.Auction;
    }

    /// <summary>
    /// A leftover stack after a partial buyout is a new listing. A refunded
    /// standing bid must not stay on it or cancel/expire will treat it as live.
    /// </summary>
    public static void ClearStandingBid(AuctionLot lot)
    {
        if (lot == null)
            return;
        lot.BidderId = 0;
        lot.BidderName = string.Empty;
        lot.BidMoney = 0;
        lot.BidWorldId = UnsetWorldId;
        lot.IsDirty = true;
    }

    public static long DisplayPrice(AuctionLot lot)
    {
        if (lot == null)
            return 0;
        if (lot.DirectMoney > 0)
            return lot.DirectMoney;
        if (lot.BidMoney > 0)
            return lot.BidMoney;
        return lot.StartMoney;
    }

    public static bool Matches(AuctionLot lot, AuctionSearch search, IReadOnlyList<uint> templateIds, string localizedName) =>
        Matches(lot, search, templateIds, string.IsNullOrEmpty(localizedName) ? [] : [localizedName]);

    public static bool Matches(AuctionLot lot, AuctionSearch search, IReadOnlyList<uint> templateIds, IReadOnlyList<string> localizedNames)
    {
        if (lot?.Item == null || search == null)
            return false;

        if (search.ClientId != 0 && lot.ClientId != (uint)search.ClientId)
            return false;

        if (search.WorldId != 0 && search.WorldId != UnsetWorldId && lot.WorldId != search.WorldId)
            return false;

        if (templateIds is { Count: > 0 })
        {
            if (!templateIds.Contains(lot.Item.TemplateId))
                return false;
        }
        else if (!KeywordMatches(search, KeywordNames(localizedNames, lot.Item.Template?.Name)))
        {
            return false;
        }

        var settings = lot.Item.Template?.AuctionSettings;
        if (search.CategoryA != 0 && settings != null && settings.CategoryA != search.CategoryA)
            return false;
        if (search.CategoryB != 0 && settings != null && settings.CategoryB != search.CategoryB)
            return false;
        if (search.CategoryC != 0 && settings != null && settings.CategoryC != search.CategoryC)
            return false;

        if (search.Grade != 0 && lot.Item.Grade != search.Grade)
            return false;

        var level = lot.Item.Template?.Level ?? 0;
        if (search.MaxItemLevel != 0 && level > search.MaxItemLevel)
            return false;
        if (search.MinItemLevel != 0 && level < search.MinItemLevel)
            return false;

        var price = DisplayPrice(lot);
        if (search.MinPrice > 0 && price < search.MinPrice)
            return false;
        if (search.MaxPrice > 0 && price > search.MaxPrice)
            return false;

        return true;
    }

    /// <summary>
    /// Name search is only for the keyword-only packet. The multilingual packet already
    /// resolved the typed name to template ids, so a second string check would drop the
    /// same lots. Keyword-only matches every loaded <c>localized_texts</c> language.
    /// </summary>
    public static bool KeywordMatches(AuctionSearch search, params string[] names)
    {
        if (search == null || string.IsNullOrEmpty(search.Keyword))
            return true;

        foreach (var name in names)
        {
            if (string.IsNullOrEmpty(name))
                continue;
            if (search.ExactMatch)
            {
                if (name.Equals(search.Keyword, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (name.Contains(search.Keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string[] KeywordNames(IReadOnlyList<string> localizedNames, string templateName)
    {
        var count = (localizedNames?.Count ?? 0) + 1;
        var names = new string[count];
        var i = 0;
        if (localizedNames != null)
        {
            foreach (var name in localizedNames)
                names[i++] = name;
        }

        names[i] = templateName;
        return names;
    }

    public static IEnumerable<AuctionLot> Sort(IEnumerable<AuctionLot> lots, AuctionSearchSortKind kind, AuctionSearchSortOrder order)
    {
        var source = lots ?? [];
        return kind switch
        {
            AuctionSearchSortKind.BidPrice => order == AuctionSearchSortOrder.Asc
                ? source.OrderBy(o => o.BidMoney)
                : source.OrderByDescending(o => o.BidMoney),
            AuctionSearchSortKind.DirectPrice => order == AuctionSearchSortOrder.Asc
                ? source.OrderBy(o => o.DirectMoney)
                : source.OrderByDescending(o => o.DirectMoney),
            AuctionSearchSortKind.ExpireDate => order == AuctionSearchSortOrder.Asc
                ? source.OrderBy(o => o.EndTime)
                : source.OrderByDescending(o => o.EndTime),
            AuctionSearchSortKind.ItemLevel => order == AuctionSearchSortOrder.Asc
                ? source.OrderBy(o => o.Item?.Template?.Level ?? 0)
                : source.OrderByDescending(o => o.Item?.Template?.Level ?? 0),
            AuctionSearchSortKind.ItemName => order == AuctionSearchSortOrder.Asc
                ? source.OrderBy(o => o.Item?.Template?.Name ?? string.Empty)
                : source.OrderByDescending(o => o.Item?.Template?.Name ?? string.Empty),
            _ => order == AuctionSearchSortOrder.Asc
                ? source.OrderBy(o => o.Id)
                : source.OrderByDescending(o => o.Id)
        };
    }

    public static IReadOnlyList<AuctionLot> Page(IReadOnlyList<AuctionLot> lots, int page)
    {
        if (lots == null || lots.Count == 0 || page < 0)
            return [];

        var start = page * SearchPageSize;
        if (start >= lots.Count)
            return [];

        var take = Math.Min(SearchPageSize, lots.Count - start);
        if (start == 0 && take == lots.Count)
            return lots;

        var pageLots = new List<AuctionLot>(take);
        for (var i = 0; i < take; i++)
            pageLots.Add(lots[start + i]);
        return pageLots;
    }

    public static int ToMailCopper(long amount) =>
        (int)Math.Clamp(amount, 0, int.MaxValue);
}
