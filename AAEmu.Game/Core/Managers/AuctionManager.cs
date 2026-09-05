using System.Collections.Concurrent;
using System.Text;

using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Auction;
using AAEmu.Game.Models.Game.Auction.Templates;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Features;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Mails;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class AuctionManager(
    IItemManager itemManager,
    INameManager nameManager,
    IAuctionIdManager auctionIdManager,
    ILocalizationManager localizationManager,
    ITaskManager taskManager) : Singleton<AuctionManager>, IAuctionManager
{
    private static Logger Logger { get; } = LogManager.GetLogger("AuctionHouse");

    public ConcurrentDictionary<ulong, AuctionLot> AuctionLots { get; } = [];
    public ConcurrentBag<long> _deletedAuctionItemIds { get; } = [];

    public AuctionFeeSchedule Fees { get; } = new();

    private readonly List<AuctionSale> _soldRecords = [];
    private readonly object _soldLock = new();
    private readonly object _houseLock = new();
    private readonly HashSet<ulong> _listedItemIds = [];
    private bool? _houseHasExtendedColumns;
    private bool _tickStarted;

    private bool HouseIsOpen => AppConfiguration.Instance.InitialConfig.CanUseAuction;

    private bool AllowPartialBuy =>
        FeaturesManager.Fsets != null && FeaturesManager.Fsets.Check(Feature.auctionPartialBuy);

    private bool HasAuctionPostBuff(Character player)
    {
        if (player == null)
            return false;
        var worldId = AppConfiguration.Instance.Id;
        return AccountAttributeManager.Instance.Get(player.AccountId, worldId)
            .Any(a => a.KindId == (uint)AccountAttributeKind.AuctionPost);
    }

    private int ChargeDiscount(Character player) =>
        HasAuctionPostBuff(player) ? Fees.SaleChargeAccountBuffDiscount : 0;

    private int DepositDiscount(Character player) =>
        HasAuctionPostBuff(player) ? Fees.DepositAccountBuffDiscount : 0;

    private string LocalizedItemName(uint templateId)
    {
        var template = itemManager.GetTemplate(templateId);
        return localizationManager.Get("items", "name", templateId, template?.Name ?? string.Empty);
    }

    private IReadOnlyList<string> LocalizedItemNames(uint templateId) =>
        localizationManager.GetAll("items", "name", templateId) ?? [];

    private static byte ServerWorldId => AppConfiguration.Instance.Id;

    private void SendHouseMessage(uint characterId, AuctionMessageKind kind, uint templateId, long money)
    {
        var online = WorldManager.Instance.GetCharacterById(characterId);
        online?.SendPacket(new SCAuctionMessagePacket(kind, templateId, money));
    }

    private void SendSearchPage(Character player, int page, IReadOnlyList<AuctionLot> lots, ErrorMessageType error = ErrorMessageType.NoErrorMessage)
    {
        player.SendPacket(new SCAuctionSearchedPacket(page, lots, (short)error, DateTime.UtcNow));
    }

    private void RecordSale(AuctionLot lot, long soldAmount, Item sold = null)
    {
        var item = sold ?? lot?.Item;
        if (item == null || soldAmount <= 0)
            return;

        var sale = new AuctionSale(item.TemplateId, item.Grade, DateTime.UtcNow, soldAmount, item.Count);
        lock (_soldLock)
        {
            _soldRecords.Add(sale);
            TrimSoldRecordsLocked(DateTime.UtcNow);
        }

        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO auction_sold_records (item_template_id, item_grade, sold_at, price, stack) " +
                "VALUES (@template, @grade, @sold_at, @price, @stack)";
            command.Parameters.AddWithValue("@template", sale.ItemTemplateId);
            command.Parameters.AddWithValue("@grade", sale.Grade);
            command.Parameters.AddWithValue("@sold_at", sale.SoldAt);
            command.Parameters.AddWithValue("@price", sale.Price);
            command.Parameters.AddWithValue("@stack", sale.Stack);
            command.Prepare();
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to persist sold record template={0} grade={1} price={2}",
                sale.ItemTemplateId, sale.Grade, sale.Price);
        }
    }

    private void TrimSoldRecordsLocked(DateTime utcNow)
    {
        var cutoff = utcNow.Date.AddDays(1 - AuctionHouseRules.SoldRecordDays);
        _soldRecords.RemoveAll(s => s.SoldAt < cutoff);
    }

    private void UntrackItem(ulong itemId)
    {
        if (itemId != 0)
            _listedItemIds.Remove(itemId);
    }

    private bool TryAddLot_NoLock(AuctionLot lot)
    {
        if (lot?.Item == null || !AuctionHouseRules.IsHeldByHouse(lot.Item))
            return false;
        if (!_listedItemIds.Add(lot.Item.Id))
        {
            Logger.Warn("Refuse lot {0}: item {1} is already listed", lot.Id, lot.Item.Id);
            return false;
        }

        if (AuctionLots.TryAdd(lot.Id, lot))
            return true;

        UntrackItem(lot.Item.Id);
        Logger.Warn("Unable to add lot {0}, possible duplicate Id", lot.Id);
        return false;
    }

    private bool TryDetachLot_NoLock(ulong lotId, out AuctionLot lot)
    {
        if (!AuctionLots.TryRemove(lotId, out lot))
            return false;
        if (lot.Item != null)
            UntrackItem(lot.Item.Id);
        return true;
    }

    private void ForgetLot_NoLock(AuctionLot lot)
    {
        if (lot.Item != null)
            UntrackItem(lot.Item.Id);
        auctionIdManager.ReleaseId((uint)lot.Id);
        _deletedAuctionItemIds.Add((long)lot.Id);
    }

    private void RestoreLot_NoLock(AuctionLot lot)
    {
        if (!TryAddLot_NoLock(lot))
            Logger.Error("Failed to restore lot {0} after an aborted settle", lot.Id);
    }

    private void SendBidRefund(uint bidderId, AuctionLot lot, long amount)
    {
        if (bidderId == 0 || amount <= 0)
            return;

        var mail = new MailForAuction(lot.Item?.TemplateId ?? 0, lot.ClientId, lot.DirectMoney, 0);
        if (mail.FinalizeForBidFail(bidderId, amount))
            mail.Send();
    }

    private void SettleSold_NoLock(AuctionLot lot, string buyer, long soldAmount)
    {
        var item = lot.Item != null ? itemManager.GetItemByItemId(lot.Item.Id) : null;
        var buyerId = nameManager.GetCharacterId(buyer);
        if (item == null || buyerId == 0 || !AuctionHouseRules.IsHeldByHouse(item))
        {
            if (buyerId != 0)
                SendBidRefund(buyerId, lot, soldAmount);
            Logger.Error("Sale aborted lot={0} buyer={1}: item is not in house escrow", lot.Id, buyer);
            ForgetLot_NoLock(lot);
            return;
        }

        var listingDeposit = Fees.GetListingDeposit(lot.DirectMoney, lot.Duration);
        var buyMail = new MailForAuction(item, lot.ClientId, soldAmount, listingDeposit);
        if (!buyMail.FinalizeForSaleBuyer(buyerId))
        {
            AbortSaleDelivery_NoLock(lot, item, buyerId, soldAmount, "buyer name");
            return;
        }

        if (!buyMail.Send())
        {
            buyMail.RevertBuyerClaim();
            AbortSaleDelivery_NoLock(lot, item, buyerId, soldAmount, "buyer mail");
            return;
        }

        var saleCharge = AuctionHouseRules.SaleChargeForLot(
            Fees, soldAmount, lot.ChargePercent, item.Template?.AuctionSettings?.EffectiveChargeRate ?? 0);
        var moneyAfterFee = soldAmount - saleCharge;
        if (!string.IsNullOrEmpty(lot.ClientName))
        {
            var sellMail = new MailForAuction(item, lot.ClientId, soldAmount, listingDeposit);
            if (sellMail.FinalizeForSaleSeller(moneyAfterFee, saleCharge))
                sellMail.Send();
            else
                Logger.Error("Seller mail failed lot={0} seller={1} after the item was delivered", lot.Id, lot.ClientId);
        }

        Logger.Info("Sale settle lot={0} seller={1} ({2}) buyer={3} sold={4} net={5} charge={6}",
            lot.Id, lot.ClientName, lot.ClientId, buyer, soldAmount, moneyAfterFee, saleCharge);
        SendHouseMessage(lot.ClientId, AuctionMessageKind.Sold, item.TemplateId, soldAmount);
        RecordSale(lot, soldAmount, item);
        ForgetLot_NoLock(lot);
    }

    private void SettleSoldSlice_NoLock(AuctionLot lot, Item sold, string buyer, long soldAmount)
    {
        var leftover = lot.Item != null ? itemManager.GetItemByItemId(lot.Item.Id) : null;
        var buyerId = nameManager.GetCharacterId(buyer);
        if (sold == null || buyerId == 0 || !AuctionHouseRules.IsHeldByHouse(sold))
        {
            MergeHouseStack(leftover, sold);
            if (buyerId != 0)
                SendBidRefund(buyerId, lot, soldAmount);
            Logger.Error("Partial sale aborted lot={0} buyer={1}: slice is not in house escrow", lot.Id, buyer);
            return;
        }

        var listingDeposit = Fees.GetListingDeposit(lot.DirectMoney, lot.Duration);
        var buyMail = new MailForAuction(sold, lot.ClientId, soldAmount, listingDeposit);
        if (!buyMail.FinalizeForSaleBuyer(buyerId))
        {
            MergeHouseStack(leftover, sold);
            SendBidRefund(buyerId, lot, soldAmount);
            Logger.Error("Partial sale aborted lot={0} buyer={1}: buyer name", lot.Id, buyerId);
            return;
        }

        if (!buyMail.Send())
        {
            buyMail.RevertBuyerClaim();
            MergeHouseStack(leftover, sold);
            SendBidRefund(buyerId, lot, soldAmount);
            Logger.Error("Partial sale aborted lot={0} buyer={1}: buyer mail", lot.Id, buyerId);
            return;
        }

        var saleCharge = AuctionHouseRules.SaleChargeForLot(
            Fees, soldAmount, lot.ChargePercent, sold.Template?.AuctionSettings?.EffectiveChargeRate ?? 0);
        var moneyAfterFee = soldAmount - saleCharge;
        if (!string.IsNullOrEmpty(lot.ClientName))
        {
            var sellMail = new MailForAuction(sold, lot.ClientId, soldAmount, listingDeposit);
            if (sellMail.FinalizeForSaleSeller(moneyAfterFee, saleCharge))
                sellMail.Send();
            else
                Logger.Error("Seller mail failed lot={0} seller={1} after a partial delivery", lot.Id, lot.ClientId);
        }

        Logger.Info("Partial sale settle lot={0} seller={1} ({2}) buyer={3} sold={4} leftover={5} net={6} charge={7}",
            lot.Id, lot.ClientName, lot.ClientId, buyer, soldAmount, leftover?.Count ?? 0, moneyAfterFee, saleCharge);
        SendHouseMessage(lot.ClientId, AuctionMessageKind.Sold, sold.TemplateId, soldAmount);
        RecordSale(lot, soldAmount, sold);
        AuctionHouseRules.ClearStandingBid(lot);
        if (leftover != null)
        {
            var stacks = AuctionHouseRules.ClampStacks(leftover.Count, lot.MinStack, lot.MaxStack, AllowPartialBuy);
            lot.MinStack = stacks.minStack;
            lot.MaxStack = stacks.maxStack;
            lot.IsDirty = true;
        }
    }

    private Item TrySplitHouseStack(Item source, int amount)
    {
        if (source == null || !ItemSplitRules.IsSplitAmount(source.Count, amount))
            return null;

        var split = itemManager.Create(source.TemplateId, amount, source.Grade);
        if (split == null)
            return null;

        ItemSplitRules.CopyStackFields(source, split);
        split.TemplateId = source.TemplateId;
        split.Template = source.Template;
        split.Grade = source.Grade;
        split.Count = amount;
        var before = source.Count;
        source.Count -= amount;
        if (!ItemSplitRules.ConservesCount(before, amount, source.Count, split.Count))
        {
            source.Count = before;
            itemManager.ReleaseId(split.Id);
            return null;
        }

        AuctionHouseRules.ReturnToHouseEscrow(split, source.OwnerId);
        return split;
    }

    private void MergeHouseStack(Item leftover, Item slice)
    {
        if (leftover != null && slice != null && leftover.Id != slice.Id)
        {
            leftover.Count += slice.Count;
            AuctionHouseRules.ReturnToHouseEscrow(leftover, leftover.OwnerId);
            itemManager.ReleaseId(slice.Id);
            return;
        }

        if (slice != null)
            AuctionHouseRules.ReturnToHouseEscrow(slice, leftover?.OwnerId ?? slice.OwnerId);
    }

    /// <summary>
    /// Undo a delivery that never reached the buyer. By this point every copper bid on the
    /// lot has been refunded: an outbid bidder in <see cref="BidOnAuctionLot"/>, the buyer's
    /// own earlier bid inside <paramref name="soldAmount"/>, or the expiry winner here. The
    /// restored listing must therefore carry no standing bid, or expiry would hand the item
    /// to a bidder who already has their money back.
    /// </summary>
    private void AbortSaleDelivery_NoLock(AuctionLot lot, Item item, uint buyerId, long soldAmount, string reason)
    {
        SendBidRefund(buyerId, lot, soldAmount);
        AuctionHouseRules.ReturnToHouseEscrow(item, lot.ClientId);
        AuctionHouseRules.ClearStandingBid(lot);
        RestoreLot_NoLock(lot);
        Logger.Error("Sale aborted lot={0} buyer={1}: {2}; listing restored without a bid", lot.Id, buyerId, reason);
    }

    private void SettleExpire_NoLock(AuctionLot lot)
    {
        if (lot.BidderId != 0)
        {
            SettleSold_NoLock(lot, lot.BidderName, lot.BidMoney);
            return;
        }

        var item = lot.Item != null ? itemManager.GetItemByItemId(lot.Item.Id) : null;
        if (item != null && AuctionHouseRules.IsHeldByHouse(item) && !string.IsNullOrEmpty(lot.ClientName))
        {
            var listingDeposit = Fees.GetListingDeposit(lot.DirectMoney, lot.Duration);
            var failMail = new MailForAuction(item, lot.ClientId, lot.DirectMoney, listingDeposit);
            if (failMail.FinalizeForFail())
                failMail.Send();
            Logger.Info("Expire unsold lot={0} seller={1} ({2}) item={3}",
                lot.Id, lot.ClientName, lot.ClientId, item.Id);
        }
        else
        {
            Logger.Warn("Expire lot={0} with no escrowed item", lot.Id);
        }

        ForgetLot_NoLock(lot);
    }

    public void CancelAuctionLot(Character player, ulong auctionId)
    {
        if (player == null)
            return;

        if (!HouseIsOpen)
        {
            player.SendErrorMessage(ErrorMessageType.AucPermissionDeny);
            return;
        }

        lock (_houseLock)
        {
            using var persist = MailManager.Instance.DeferPersist();
            var auctionLot = GetAuctionLotFromId(auctionId);
            if (auctionLot == null)
            {
                Logger.Warn("Cancel refused {0} ({1}): lot {2} missing", player.Name, player.Id, auctionId);
                player.SendErrorMessage(ErrorMessageType.AucRefreshDisplay);
                return;
            }

            if (auctionLot.ClientId != player.Id)
            {
                Logger.Warn("Cancel refused {0} ({1}): lot {2} owned by {3}",
                    player.Name, player.Id, auctionId, auctionLot.ClientId);
                player.SendErrorMessage(ErrorMessageType.AucPermissionDeny);
                return;
            }

            if (auctionLot.BidderId != 0 || auctionLot.BidMoney > 0)
            {
                Logger.Info("Cancel refused {0} ({1}): lot {2} already has a bid", player.Name, player.Id, auctionId);
                player.SendErrorMessage(ErrorMessageType.AucCannotCancelIfBid);
                return;
            }

            if (!TryDetachLot_NoLock(auctionId, out auctionLot))
            {
                player.SendErrorMessage(ErrorMessageType.AucRefreshDisplay);
                return;
            }

            var listedItem = auctionLot.Item != null ? itemManager.GetItemByItemId(auctionLot.Item.Id) : null;
            if (listedItem == null || !AuctionHouseRules.IsHeldByHouse(listedItem))
            {
                Logger.Error("Cancel lot={0} had no escrowed item", auctionId);
                ForgetLot_NoLock(auctionLot);
                player.SendErrorMessage(ErrorMessageType.AucRefreshDisplay);
                return;
            }

            var listingDeposit = Fees.GetListingDeposit(auctionLot.DirectMoney, auctionLot.Duration);
            if (!player.Inventory.MailAttachments.AddOrMoveExistingItem(ItemTaskType.Auction, listedItem))
            {
                RestoreLot_NoLock(auctionLot);
                player.SendErrorMessage(ErrorMessageType.AucInternalError);
                return;
            }

            var cancelMail = new MailForAuction(listedItem, auctionLot.ClientId, auctionLot.DirectMoney, listingDeposit);
            if (!cancelMail.FinalizeForCancel() || !cancelMail.Send())
            {
                player.Inventory.AuctionAttachments.AddOrMoveExistingItem(ItemTaskType.Auction, listedItem);
                RestoreLot_NoLock(auctionLot);
                player.SendErrorMessage(ErrorMessageType.AucInternalError);
                return;
            }

            Logger.Info("Cancel lot={0} seller={1} ({2}) item={3}",
                auctionLot.Id, player.Name, player.Id, listedItem.Id);
            ForgetLot_NoLock(auctionLot);
            player.SendPacket(new SCAuctionCanceledPacket(auctionLot));
        }
    }

    private AuctionLot GetAuctionLotFromId(ulong auctionId) =>
        AuctionLots.GetValueOrDefault(auctionId);

    public void BidOnAuctionLot(Character player, AuctionBid bid)
    {
        if (player == null || bid == null)
            return;

        if (!HouseIsOpen)
        {
            player.SendErrorMessage(ErrorMessageType.AucPermissionDeny);
            return;
        }

        lock (_houseLock)
        {
            // One snapshot after the charge, the refund letter, the bid replacement or the
            // settle. The refund mail alone used to force a save that still held the bid it
            // had just refunded.
            using var persist = MailManager.Instance.DeferPersist();
            var auctionLot = GetAuctionLotFromId(bid.LotId);
            if (auctionLot?.Item == null || DateTime.UtcNow >= auctionLot.EndTime)
            {
                Logger.Warn("Bid refused {0} ({1}): lot {2} missing or expired", player.Name, player.Id, bid.LotId);
                player.SendErrorMessage(ErrorMessageType.AucRefreshDisplay);
                return;
            }

            var liveItem = itemManager.GetItemByItemId(auctionLot.Item.Id);
            if (!AuctionHouseRules.IsHeldByHouse(liveItem))
            {
                Logger.Warn("Bid refused {0} ({1}): lot {2} item is not in escrow", player.Name, player.Id, bid.LotId);
                player.SendErrorMessage(ErrorMessageType.AucRefreshDisplay);
                return;
            }

            if (auctionLot.ClientId == player.Id)
            {
                player.SendErrorMessage(ErrorMessageType.AucBidSelf);
                return;
            }

            var bidderIsCurrent = auctionLot.BidderId == player.Id;
            if (!AuctionHouseRules.TryGetBidCharge(
                    bid.Money,
                    auctionLot.StartMoney,
                    auctionLot.BidMoney,
                    auctionLot.DirectMoney,
                    bidderIsCurrent,
                    out var charge,
                    out var isBuyout))
            {
                if (AuctionHouseRules.IsBuyoutOffer(bid.Money, auctionLot.DirectMoney))
                    player.SendErrorMessage(ErrorMessageType.AuctionInvalidBidPrice);
                else if (auctionLot.StartMoney > 0 && bid.Money < auctionLot.StartMoney)
                    player.SendErrorMessage(ErrorMessageType.AucBidMoneyUnderStartMoney);
                else
                    player.SendErrorMessage(ErrorMessageType.AucBidMoneyUnderTopMost);
                return;
            }

            var stack = AuctionHouseRules.ResolveBidStack(
                bid.StackSize, liveItem.Count, auctionLot.MinStack, auctionLot.MaxStack, AllowPartialBuy);
            bid.StackSize = stack;

            if (!player.SubtractMoney(SlotType.Inventory, charge, ItemTaskType.Auction))
            {
                player.SendErrorMessage(ErrorMessageType.NotEnoughMoney);
                return;
            }

            var previousBidderId = auctionLot.BidderId;
            var previousBid = auctionLot.BidMoney;
            if (previousBidderId != 0 && previousBidderId != player.Id)
            {
                SendBidRefund(previousBidderId, auctionLot, previousBid);
                SendHouseMessage(previousBidderId, AuctionMessageKind.Outbid, auctionLot.Item.TemplateId, previousBid);
                Logger.Info("Outbid lot={0} previous={1} refund={2} by {3} ({4})",
                    auctionLot.Id, previousBidderId, previousBid, player.Name, player.Id);
                // The refunded bid must not survive on the lot: a leftover stack is a fresh
                // listing, and a full buyout that fails delivery is restored as one.
                AuctionHouseRules.ClearStandingBid(auctionLot);
            }

            var standing = isBuyout ? auctionLot.DirectMoney : bid.Money;
            bid.LotId = auctionLot.Id;
            bid.BidderName = player.Name;
            bid.BidderId = player.Id;
            bid.WorldId = ServerWorldId;
            bid.Money = standing;

            if (isBuyout)
            {
                var delivered = liveItem;
                if (stack < liveItem.Count)
                {
                    delivered = TrySplitHouseStack(liveItem, stack);
                    if (delivered == null)
                    {
                        player.AddMoney(SlotType.Inventory, charge, ItemTaskType.Auction);
                        player.SendErrorMessage(ErrorMessageType.AucInternalError);
                        return;
                    }

                    Logger.Info("Partial buyout lot={0} buyer={1} ({2}) stack={3} leftover={4} price={5}",
                        auctionLot.Id, player.Name, player.Id, stack, liveItem.Count, standing);
                    player.SendPacket(new SCAuctionBidPacket(bid, true, delivered.TemplateId));
                    SettleSoldSlice_NoLock(auctionLot, delivered, player.Name, standing);
                    return;
                }

                if (!TryDetachLot_NoLock(auctionLot.Id, out auctionLot))
                {
                    player.AddMoney(SlotType.Inventory, charge, ItemTaskType.Auction);
                    player.SendErrorMessage(ErrorMessageType.AucRefreshDisplay);
                    return;
                }

                Logger.Info("Buyout lot={0} buyer={1} ({2}) price={3} charged={4} item={5}",
                    auctionLot.Id, player.Name, player.Id, standing, charge, liveItem.Id);
                player.SendPacket(new SCAuctionBidPacket(bid, true, liveItem.TemplateId));
                SettleSold_NoLock(auctionLot, player.Name, standing);
                return;
            }

            auctionLot.BidderName = player.Name;
            auctionLot.BidderId = player.Id;
            auctionLot.BidWorldId = ServerWorldId;
            auctionLot.BidMoney = standing;
            auctionLot.IsDirty = true;

            Logger.Info("Bid lot={0} bidder={1} ({2}) standing={3} charged={4}",
                auctionLot.Id, player.Name, player.Id, standing, charge);
            player.SendPacket(new SCAuctionBidPacket(bid, false, liveItem.TemplateId));
            MailManager.Instance.PersistNow();
        }
    }

    public void GetBidAuctionLots(Character player, int page)
    {
        if (player == null)
            return;

        if (!HouseIsOpen)
        {
            SendSearchPage(player, page, [], ErrorMessageType.AucPermissionDeny);
            return;
        }

        var mine = AuctionLots.Values.Where(lot => lot.BidderId == player.Id).ToList();
        var sorted = AuctionHouseRules.Sort(mine, AuctionSearchSortKind.Default, AuctionSearchSortOrder.Asc).ToList();
        SendSearchPage(player, page, AuctionHouseRules.Page(sorted, page));
    }

    public void CheapestAuctionLot(Character player, uint templateId, byte itemGrade = 0)
    {
        if (player == null)
            return;

        if (!HouseIsOpen)
            return;

        var cheapest = AuctionLots.Values
            .Where(lot => lot.Item != null
                          && lot.Item.TemplateId == templateId
                          && (itemGrade == 0 || lot.Item.Grade == itemGrade))
            .OrderBy(AuctionHouseRules.DisplayPrice)
            .FirstOrDefault();

        player.SendPacket(new SCAuctionLowestPricePacket(templateId, itemGrade, cheapest == null ? 0 : AuctionHouseRules.DisplayPrice(cheapest)));
    }

    public void SearchSoldRecords(Character player, uint templateId, byte grade, bool askMarketPriceUi)
    {
        if (player == null)
            return;

        if (!HouseIsOpen)
            return;

        List<AuctionSale> snapshot;
        lock (_soldLock)
        {
            TrimSoldRecordsLocked(DateTime.UtcNow);
            snapshot = _soldRecords.ToList();
        }

        var days = AuctionSoldRecordRules.BuildDays(snapshot, templateId, grade, DateTime.UtcNow);
        player.SendPacket(new SCAuctionSoldRecordSearchedPacket(templateId, grade, askMarketPriceUi, days));
    }

    public void AddAuctionLot(AuctionLot lot)
    {
        lock (_houseLock)
        {
            if (!TryAddLot_NoLock(lot))
                auctionIdManager.ReleaseId((uint)lot.Id);
        }
    }

    public void UpdateAuctionHouse()
    {
        lock (_houseLock)
        {
            Logger.Trace("Updating Auction House");
            using var persist = MailManager.Instance.DeferPersist();
            var itemsToRemove = AuctionLots.Values.Where(c => DateTime.UtcNow > c.EndTime).ToList();
            foreach (var lot in itemsToRemove)
            {
                if (!TryDetachLot_NoLock(lot.Id, out var detached))
                    continue;
                SettleExpire_NoLock(detached);
            }
        }
    }

    public AuctionLot CreateAuctionLot(
        uint playerId,
        string playerName,
        Item itemToList,
        long startPrice,
        long buyoutPrice,
        AuctionDuration duration,
        int minStack = 1,
        int maxStack = 1)
    {
        var now = DateTime.UtcNow;
        var stacks = AuctionHouseRules.ClampStacks(itemToList?.Count ?? 1, minStack, maxStack, AllowPartialBuy);
        return new AuctionLot
        {
            Id = auctionIdManager.GetNextId(),
            Duration = duration,
            Item = itemToList,
            EndTime = now.AddHours(AuctionHouseRules.HoursFor(duration)),
            WorldId = ServerWorldId,
            ClientId = playerId,
            ClientName = playerName ?? string.Empty,
            StartMoney = startPrice,
            DirectMoney = buyoutPrice,
            PostDate = now,
            Asked = (ulong)Helpers.UnixTime(now),
            ChargePercent = Fees.SaleChargeRate,
            DepositPercent = Fees.GetDepositRate(duration),
            ServiceKind = 0,
            BidWorldId = AuctionHouseRules.UnsetWorldId,
            BidderId = 0,
            BidderName = string.Empty,
            BidMoney = 0,
            ExtraMoney = 0,
            MinStack = stacks.minStack,
            MaxStack = stacks.maxStack,
            IsDirty = true
        };
    }

    public void Load()
    {
        try
        {
            lock (_houseLock)
            {
                AuctionLots.Clear();
                _listedItemIds.Clear();
                _deletedAuctionItemIds.Clear();
            }

            lock (_soldLock)
                _soldRecords.Clear();

            Fees.Load();

            using (var connection = MySQL.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM auction_house";
                    command.Prepare();
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        var itemId = reader.GetUInt64("item_id");
                        var auctionLot = new AuctionLot
                        {
                            Id = reader.GetUInt64("id"),
                            Duration = (AuctionDuration)reader.GetByte("duration"),
                            Item = itemManager.GetItemByItemId(itemId),
                            PostDate = reader.GetDateTime("post_date"),
                            EndTime = reader.GetDateTime("end_time"),
                            WorldId = reader.GetByte("world_id"),
                            ClientId = reader.GetUInt32("client_id"),
                            ClientName = reader.GetString("client_name"),
                            StartMoney = reader.GetInt64("start_money"),
                            DirectMoney = reader.GetInt64("direct_money"),
                            Asked = ReadOptionalUInt64(reader, "asked"),
                            ChargePercent = ReadOptionalInt32(reader, "charge_percent"),
                            DepositPercent = ReadOptionalInt32(reader, "deposit_percent"),
                            ServiceKind = ReadOptionalByte(reader, "service_kind"),
                            BidWorldId = (byte)reader.GetInt32("bid_world_id"),
                            BidderId = reader.GetUInt32("bidder_id"),
                            BidderName = reader.GetString("bidder_name"),
                            BidMoney = reader.GetInt64("bid_money"),
                            ExtraMoney = reader.GetInt64("extra"),
                            MinStack = ReadOptionalInt32(reader, "min_stack", 1),
                            MaxStack = ReadOptionalInt32(reader, "max_stack", 1),
                        };
                        if (auctionLot.Asked == 0)
                            auctionLot.Asked = (ulong)Helpers.UnixTime(auctionLot.PostDate);
                        if (auctionLot.Item == null || !AuctionHouseRules.IsHeldByHouse(auctionLot.Item))
                        {
                            Logger.Warn("Skip lot {0}: item {1} is not in house escrow", auctionLot.Id, itemId);
                            _deletedAuctionItemIds.Add((long)auctionLot.Id);
                            auctionIdManager.ReleaseId((uint)auctionLot.Id);
                            continue;
                        }

                        lock (_houseLock)
                        {
                            if (!TryAddLot_NoLock(auctionLot))
                            {
                                Logger.Warn("Skip lot {0}: item {1} is already listed", auctionLot.Id, itemId);
                                _deletedAuctionItemIds.Add((long)auctionLot.Id);
                                auctionIdManager.ReleaseId((uint)auctionLot.Id);
                            }
                        }
                    }
                }

                LoadSoldRecords(connection);
            }

            if (!_tickStarted)
            {
                taskManager.Schedule(new AuctionHouseTask(), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
                _tickStarted = true;
            }

            Logger.Info("Loaded {0} lots", AuctionLots.Count);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to load auction data");
        }
    }

    private void LoadSoldRecords(MySqlConnection connection)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT item_template_id, item_grade, sold_at, price, stack FROM auction_sold_records " +
                "WHERE sold_at >= @cutoff";
            command.Parameters.AddWithValue("@cutoff", DateTime.UtcNow.Date.AddDays(1 - AuctionHouseRules.SoldRecordDays));
            command.Prepare();
            using var reader = command.ExecuteReader();
            lock (_soldLock)
            {
                while (reader.Read())
                {
                    _soldRecords.Add(new AuctionSale(
                        reader.GetUInt32("item_template_id"),
                        reader.GetByte("item_grade"),
                        reader.GetDateTime("sold_at"),
                        reader.GetInt64("price"),
                        reader.GetInt32("stack")));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "auction_sold_records is missing or unreadable; sold history stays empty until the table exists");
        }
    }

    private static bool HasColumn(MySqlDataReader reader, string name)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static int ReadOptionalInt32(MySqlDataReader reader, string name, int fallback = 0) =>
        HasColumn(reader, name) && !reader.IsDBNull(reader.GetOrdinal(name)) ? reader.GetInt32(name) : fallback;

    private static byte ReadOptionalByte(MySqlDataReader reader, string name, byte fallback = 0) =>
        HasColumn(reader, name) && !reader.IsDBNull(reader.GetOrdinal(name)) ? reader.GetByte(name) : fallback;

    private static ulong ReadOptionalUInt64(MySqlDataReader reader, string name, ulong fallback = 0) =>
        HasColumn(reader, name) && !reader.IsDBNull(reader.GetOrdinal(name)) ? reader.GetUInt64(name) : fallback;

    public (int, int) Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        var deletedCount = 0;
        var updatedCount = 0;

        if (!_deletedAuctionItemIds.IsEmpty)
        {
            var ids = new List<long>();
            while (_deletedAuctionItemIds.TryTake(out var id))
                ids.Add(id);

            if (ids.Count > 0)
            {
                try
                {
                    using var command = connection.CreateCommand();
                    command.Connection = connection;
                    command.Transaction = transaction;
                    var names = new string[ids.Count];
                    for (var i = 0; i < ids.Count; i++)
                    {
                        names[i] = "@d" + i;
                        command.Parameters.AddWithValue(names[i], ids[i]);
                    }

                    command.CommandText = "DELETE FROM auction_house WHERE `id` IN(" + string.Join(",", names) + ")";
                    command.Prepare();
                    deletedCount = command.ExecuteNonQuery();
                }
                catch
                {
                    foreach (var id in ids)
                        _deletedAuctionItemIds.Add(id);
                    throw;
                }
            }
        }

        foreach (var lot in AuctionLots.Values.Where(c => c.IsDirty))
        {
            if (lot.Item == null)
                continue;

            if (lot.Item.SlotType == SlotType.None)
            {
                if (lot.Item.OwnerId <= 0)
                    continue;

                if (lot.Item._holdingContainer != null)
                    lot.Item.SlotType = itemManager.GetContainerSlotTypeByContainerId(lot.Item._holdingContainer.ContainerId);

                if (lot.Item.SlotType != SlotType.None)
                    Logger.Warn("Slot type for {0} was None, changing to {1}", lot.Item.Id, lot.Item.SlotType);
                else
                    continue;
            }

            if (!Enum.IsDefined(typeof(SlotType), lot.Item.SlotType))
            {
                Logger.Warn("Found SlotType.{0} in itemslist, skipping ID:{1} - Template:{2}",
                    lot.Item.SlotType, lot.Item.Id, lot.Item.TemplateId);
                continue;
            }

            using var command = connection.CreateCommand();
            command.Connection = connection;
            command.Transaction = transaction;
            var extended = HouseHasExtendedColumns(connection, transaction);
            command.CommandText = BuildInsertQuery(extended);
            AddParametersToCommand(command, lot, extended);
            command.Prepare();
            updatedCount += command.ExecuteNonQuery();
            lot.IsDirty = false;
        }

        return (updatedCount, deletedCount);
    }

    private bool HouseHasExtendedColumns(MySqlConnection connection, MySqlTransaction transaction)
    {
        if (_houseHasExtendedColumns.HasValue)
            return _houseHasExtendedColumns.Value;

        using var command = connection.CreateCommand();
        command.Connection = connection;
        command.Transaction = transaction;
        command.CommandText =
            "SELECT COUNT(*) FROM information_schema.COLUMNS " +
            "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'auction_house' AND COLUMN_NAME = 'charge_percent'";
        command.Prepare();
        var found = Convert.ToInt32(command.ExecuteScalar()) > 0;
        _houseHasExtendedColumns = found;
        if (!found)
            Logger.Warn("auction_house is missing the 10.0 listing columns; apply SQL/updates/2026-09-05_aaemu_game_auction_house_10_0.sql");
        return found;
    }

    private static string BuildInsertQuery(bool extended)
    {
        var sb = new StringBuilder();
        sb.Append("REPLACE INTO auction_house(");
        sb.Append("`id`, `duration`, `item_id`, `post_date`, `stack_size`, `end_time`, ");
        sb.Append("`world_id`, `client_id`, `client_name`, `start_money`, `direct_money`, ");
        if (extended)
            sb.Append("`asked`, `charge_percent`, `deposit_percent`, `service_kind`, ");
        sb.Append("`bid_world_id`, `bidder_id`, `bidder_name`, `bid_money`, `extra`");
        if (extended)
            sb.Append(", `min_stack`, `max_stack`");
        sb.Append(") VALUES (");
        sb.Append("@id, @duration, @item_id, @post_date, @stack_size, @end_time, ");
        sb.Append("@world_id, @client_id, @client_name, @start_money, @direct_money, ");
        if (extended)
            sb.Append("@asked, @charge_percent, @deposit_percent, @service_kind, ");
        sb.Append("@bid_world_id, @bidder_id, @bidder_name, @bid_money, @extra");
        if (extended)
            sb.Append(", @min_stack, @max_stack");
        sb.Append(" )");
        return sb.ToString();
    }

    private static void AddParametersToCommand(MySqlCommand command, AuctionLot lot, bool extended)
    {
        command.Parameters.AddWithValue("@id", lot.Id);
        command.Parameters.AddWithValue("@duration", (byte)lot.Duration);
        command.Parameters.AddWithValue("@item_id", lot.Item.Id);
        command.Parameters.AddWithValue("@post_date", lot.PostDate);
        command.Parameters.AddWithValue("@stack_size", lot.Item.Count);
        command.Parameters.AddWithValue("@end_time", lot.EndTime);
        command.Parameters.AddWithValue("@world_id", lot.WorldId);
        command.Parameters.AddWithValue("@client_id", lot.ClientId);
        command.Parameters.AddWithValue("@client_name", lot.ClientName);
        command.Parameters.AddWithValue("@start_money", lot.StartMoney);
        command.Parameters.AddWithValue("@direct_money", lot.DirectMoney);
        if (extended)
        {
            command.Parameters.AddWithValue("@asked", lot.Asked != 0 ? lot.Asked : (ulong)Helpers.UnixTime(lot.PostDate));
            command.Parameters.AddWithValue("@charge_percent", lot.ChargePercent);
            command.Parameters.AddWithValue("@deposit_percent", lot.DepositPercent);
            command.Parameters.AddWithValue("@service_kind", lot.ServiceKind);
        }
        command.Parameters.AddWithValue("@bid_world_id", lot.BidWorldId);
        command.Parameters.AddWithValue("@bidder_id", lot.BidderId);
        command.Parameters.AddWithValue("@bidder_name", lot.BidderName);
        command.Parameters.AddWithValue("@bid_money", lot.BidMoney);
        command.Parameters.AddWithValue("@extra", lot.ExtraMoney);
        if (extended)
        {
            command.Parameters.AddWithValue("@min_stack", lot.MinStack);
            command.Parameters.AddWithValue("@max_stack", lot.MaxStack);
        }
    }

    public void SearchAuctionLots(Character player, AuctionSearch search)
    {
        if (player == null || search == null)
            return;

        if (!HouseIsOpen)
        {
            SendSearchPage(player, search.Page, [], ErrorMessageType.AucPermissionDeny);
            return;
        }

        var matched = new List<AuctionLot>();
        foreach (var lot in AuctionLots.Values)
        {
            if (lot.Item?.Template == null)
                continue;

            var names = LocalizedItemNames(lot.Item.TemplateId);
            if (AuctionHouseRules.Matches(lot, search, search.ItemTemplateIds, names))
                matched.Add(lot);
        }

        var sorted = AuctionHouseRules.Sort(matched, search.SortKind, search.SortOrder).ToList();
        SendSearchPage(player, search.Page, AuctionHouseRules.Page(sorted, search.Page));
    }

    public bool PostLotOnAuction(
        Character player,
        ulong itemId,
        long startPrice,
        long buyoutPrice,
        AuctionDuration duration,
        int minStack,
        int maxStack)
    {
        if (player == null)
            return false;

        if (!HouseIsOpen)
        {
            player.SendErrorMessage(ErrorMessageType.AucPermissionDeny);
            Logger.Info("Post refused {0} ({1}): house closed", player.Name, player.Id);
            return false;
        }

        if (!AuctionHouseRules.IsValidDuration(duration))
        {
            Logger.Info("Post refused {0} ({1}): duration {2} is not a house step", player.Name, player.Id, (byte)duration);
            player.SendErrorMessage(ErrorMessageType.AucWrongDirectMoney);
            return false;
        }

        lock (_houseLock)
        {
            var item = player.Inventory?.Bag?.GetItemByItemId(itemId);
            if (item == null || !AuctionHouseRules.IsOwnedInBag(item, player.Id))
            {
                Logger.Info("Post refused {0} ({1}): item {2} missing or not in bag", player.Name, player.Id, itemId);
                player.SendErrorMessage(ErrorMessageType.AucInvalidItemOrNotInYourBag);
                return false;
            }

            if (_listedItemIds.Contains(item.Id))
            {
                Logger.Warn("Post refused {0} ({1}): item {2} is already listed", player.Name, player.Id, item.Id);
                player.SendErrorMessage(ErrorMessageType.AucInvalidItemOrNotInYourBag);
                return false;
            }

            if (!AuctionHouseRules.IsListableItem(item))
            {
                if (AuctionHouseRules.IsSoulBound(item))
                    player.SendErrorMessage(ErrorMessageType.AucSoulBoundItem);
                else if (AuctionHouseRules.HasUcc(item))
                    player.SendErrorMessage(ErrorMessageType.AuctionUccPost);
                else
                    player.SendErrorMessage(ErrorMessageType.AucNotSellable);
                return false;
            }

            if (!AuctionHouseRules.PricesAreListable(startPrice, buyoutPrice))
            {
                player.SendErrorMessage(ErrorMessageType.AucWrongDirectMoney);
                return false;
            }

            var lot = CreateAuctionLot(player.Id, player.Name, item, startPrice, buyoutPrice, duration, minStack, maxStack);
            lot.ChargePercent = AuctionHouseRules.ListingChargeRate(
                Fees.SaleChargeRate,
                item.Template?.AuctionSettings?.EffectiveChargeRate ?? 0,
                ChargeDiscount(player));
            lot.DepositPercent = AuctionFeeSchedule.ApplyPercentDiscount(Fees.GetDepositRate(duration), DepositDiscount(player));

            var auctionFee = Fees.GetListingDeposit(lot.DirectMoney, duration, DepositDiscount(player));
            if (auctionFee > 0 && !player.SubtractMoney(SlotType.Inventory, auctionFee, ItemTaskType.Auction))
            {
                auctionIdManager.ReleaseId((uint)lot.Id);
                Logger.Info("Post refused {0} ({1}): cannot pay deposit {2}", player.Name, player.Id, auctionFee);
                player.SendErrorMessage(ErrorMessageType.CanNotPutupMoney);
                return false;
            }

            if (!player.Inventory.AuctionAttachments.AddOrMoveExistingItem(ItemTaskType.Auction, item))
            {
                if (auctionFee > 0)
                    player.AddMoney(SlotType.Inventory, auctionFee, ItemTaskType.Auction);
                auctionIdManager.ReleaseId((uint)lot.Id);
                Logger.Warn("Post refused {0} ({1}): could not move item {2} into escrow", player.Name, player.Id, item.Id);
                player.SendErrorMessage(ErrorMessageType.AucInternalError);
                return false;
            }

            if (!TryAddLot_NoLock(lot))
            {
                player.Inventory.Bag.AddOrMoveExistingItem(ItemTaskType.Auction, item);
                if (auctionFee > 0)
                    player.AddMoney(SlotType.Inventory, auctionFee, ItemTaskType.Auction);
                auctionIdManager.ReleaseId((uint)lot.Id);
                player.SendErrorMessage(ErrorMessageType.AucInternalError);
                return false;
            }

            player.SendPacket(new SCAuctionPostedPacket(lot));
            Logger.Info(
                "Post lot={0} seller={1} ({2}) item={3} tpl={4} stack={5} start={6} buyout={7} duration={8} deposit={9} charge%={10} deposit%={11}",
                lot.Id, player.Name, player.Id, item.Id, item.TemplateId, item.Count,
                startPrice, buyoutPrice, duration, auctionFee, lot.ChargePercent, lot.DepositPercent);
            return true;
        }
    }
}
