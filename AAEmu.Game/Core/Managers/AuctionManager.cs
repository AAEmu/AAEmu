using System.Collections.Concurrent;
using System.Text;

using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Auction;
using AAEmu.Game.Models.Game.Auction.Templates;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Mails;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class AuctionManager(IItemManager itemManager, INameManager nameManager, IAuctionIdManager auctionIdManager, ILocalizationManager localizationManager, ITaskManager taskManager) : Singleton<AuctionManager>, IAuctionManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public ConcurrentDictionary<ulong, AuctionLot> AuctionLots { get; } = [];
    private ConcurrentBag<long> DeletedAuctionItemIds { get; } = [];

    private static int MaxListingFee => 1000000; // 100g, 100 copper coins = 1 silver, 100 silver = 1 gold.

    private void RemoveAuctionLotSold(AuctionLot itemToRemove, string buyer, int soldAmount)
    {
        if (AuctionLots.ContainsKey(itemToRemove.Id))
        {
            var newItem = itemManager.GetItemByItemId(itemToRemove.Item.Id);
            if (newItem != null)
            {
                var moneyAfterFee = soldAmount * .9;

                var recalculatedFee = itemToRemove.DirectMoney * .01 * ((int)itemToRemove.Duration + 1);
                if (recalculatedFee > MaxListingFee) recalculatedFee = MaxListingFee;

                if (itemToRemove.ClientName != "")
                {
                    var sellMail = new MailForAuction(newItem, itemToRemove.ClientId, soldAmount, (int)recalculatedFee);
                    sellMail.FinalizeForSaleSeller((int)moneyAfterFee, (int)(soldAmount - moneyAfterFee));
                    sellMail.Send();
                }

                var buyMail = new MailForAuction(newItem, itemToRemove.ClientId, soldAmount, (int)recalculatedFee);
                var buyerId = nameManager.GetCharacterId(buyer);
                buyMail.FinalizeForSaleBuyer(buyerId);
                buyMail.Send();
            }

            RemoveAuctionLot(itemToRemove);
        }
    }

    private void RemoveAuctionLotFail(AuctionLot itemToRemove)
    {
        if (!AuctionLots.ContainsKey(itemToRemove.Id))
            return;

        if (itemToRemove.BidderName != "") // Player won the bid
        {
            RemoveAuctionLotSold(itemToRemove, itemToRemove.BidderName, itemToRemove.BidMoney);
            return;
        }

        // Item did not sell by end of the timer.
        var newItem = itemManager.GetItemByItemId(itemToRemove.Item.Id);
        if (newItem != null)
        {
            // TODO: Read this from saved data
            var recalculatedFee = itemToRemove.DirectMoney * .01 * ((int)itemToRemove.Duration + 1);
            if (recalculatedFee > MaxListingFee) recalculatedFee = MaxListingFee;

            if (itemToRemove.ClientName != "")
            {
                var failMail = new MailForAuction(newItem, itemToRemove.ClientId, itemToRemove.DirectMoney,
                    (int)recalculatedFee);
                failMail.FinalizeForFail();
                failMail.Send();
            }
        }

        RemoveAuctionLot(itemToRemove);
    }

    public void CancelAuctionLot(Character player, ulong auctionId)
    {
        var auctionLot = GetAuctionLotFromId(auctionId);
        if (auctionLot == null)
        {
            Logger.Warn($"AuctionLot with ID {auctionId} not found.");
            return;
        }

        if (auctionLot.BidderName != "") // Someone has already bid on the item, and we do not want to remove it
        {
            Logger.Warn($"AuctionLot with ID {auctionId} has already been bid on.");
            return;
        }

        var newItem = itemManager.Create(auctionLot.Item.TemplateId, auctionLot.Item.Count, auctionLot.Item.Grade);
        if (newItem != null)
        {
            // itemList[0] = newItem;

            // TODO: Read this from saved data
            var recalculatedFee = auctionLot.DirectMoney * .01 * ((int)auctionLot.Duration + 1);
            if (recalculatedFee > MaxListingFee) recalculatedFee = MaxListingFee;

            var cancelMail = new MailForAuction(newItem, auctionLot.ClientId, auctionLot.DirectMoney,
                (int)recalculatedFee);
            cancelMail.FinalizeForCancel();
            cancelMail.Send();
        }

        RemoveAuctionLot(auctionLot);
        player.SendPacket(new SCAuctionCanceledPacket(auctionLot));
    }

    private AuctionLot GetAuctionLotFromId(ulong auctionId)
    {
        return AuctionLots.GetValueOrDefault(auctionId);
    }

    public void BidOnAuctionLot(Character player, uint auctioneerId, uint auctioneerId2, AuctionLot lot, AuctionBid bid)
    {
        if (player == null || lot == null || bid == null)
        {
            Logger.Warn("Invalid arguments passed to BidOnAuctionLot.");
            return;
        }

        var auctionLot = GetAuctionLotFromId(lot.Id);
        if (auctionLot == null)
        {
            Logger.Warn("Invalid auctionItem passed to BidOnAuctionLot.");
            Logger.Warn($"AuctionLot with ID {lot.Id} not found in the list.");
            return;
        }

        if (bid.Money >= auctionLot.DirectMoney && auctionLot.DirectMoney != 0) // Buy now
        {
            if (auctionLot.BidderId != 0) // send mail to person who bid if item was bought at full price.
            {
                var newMail = new MailForAuction(auctionLot.Item.TemplateId, auctionLot.ClientId, auctionLot.DirectMoney, 0);
                newMail.FinalizeForBidFail(auctionLot.BidderId, auctionLot.BidMoney);
                newMail.Send();
            }

            player.SubtractMoney(SlotType.Inventory, auctionLot.DirectMoney);
            RemoveAuctionLotSold(auctionLot, player.Name, auctionLot.DirectMoney);
        }
        else if (bid.Money > auctionLot.BidMoney) // Bid
        {
            if (auctionLot.BidderName != "" && auctionLot.BidderId != 0) // Send mail to old bidder.
            {
                // TODO: Read this from saved data
                var recalculatedFee = auctionLot.DirectMoney * .01 * ((int)auctionLot.Duration + 1);
                if (recalculatedFee > MaxListingFee) recalculatedFee = MaxListingFee;

                var cancelMail = new MailForAuction(auctionLot.Item.TemplateId, auctionLot.ClientId, auctionLot.DirectMoney, (int)recalculatedFee);
                cancelMail.FinalizeForBidFail(auctionLot.BidderId, auctionLot.BidMoney);
                cancelMail.Send();
            }

            // Set info to new bidders info
            auctionLot.BidderName = player.Name;
            auctionLot.BidderId = player.Id;
            auctionLot.BidWorldId = (byte)player.Transform.WorldId;
            auctionLot.BidMoney = bid.Money;

            bid.BidderName = player.Name;
            bid.BidderId = player.Id;
            bid.WorldId = (byte)player.Transform.WorldId;

            player.SubtractMoney(SlotType.Inventory, bid.Money, ItemTaskType.Auction);
            player.SendPacket(new SCAuctionBidPacket(bid, false, auctionLot.Item.TemplateId));
            auctionLot.IsDirty = true;

            // Updating Data in the AuctionLots List
            UpdateAuctionLotInList(auctionLot);
        }
    }

    private void UpdateAuctionLotInList(AuctionLot auctionLot)
    {
        if (auctionLot == null)
        {
            Logger.Warn("Invalid auctionItem passed to UpdateAuctionLotInList.");
            return;
        }

        var existingLot = AuctionLots.GetValueOrDefault(auctionLot.Id);
        if (existingLot != null)
        {
            // Update lot data
            existingLot.BidderName = auctionLot.BidderName;
            existingLot.BidderId = auctionLot.BidderId;
            existingLot.BidWorldId = auctionLot.BidWorldId;
            existingLot.BidMoney = auctionLot.BidMoney;
            existingLot.Item = auctionLot.Item;
            existingLot.IsDirty = auctionLot.IsDirty;
        }
        else
        {
            Logger.Warn($"AuctionLot with ID {auctionLot.Id} not found in the list.");
        }
    }

    public void GetBidAuctionLots(Character player, int page)
    {
        var searchedArticles = AuctionLots.Values.Where(lot => lot.BidderId == player.Id).ToList();
        if (searchedArticles.Count <= 0)
        {
            player.SendPacket(new SCAuctionSearchedPacket(0, 0, [], (short)ErrorMessageType.NoErrorMessage, DateTime.UtcNow));
            return;
        }

        var articles = SortArticles(searchedArticles, AuctionSearchSortKind.Default, AuctionSearchSortOrder.Asc).ToArray();
        var dividedLists = Helpers.SplitArray(articles, 9); // We split the array into arrays of 9 values each

        if (page < 0 || page >= dividedLists.Length) // Stops client DC when requesting an out-of-bounds page
        {
            Logger.Warn($"[AH-BIDS] {player.Name} requested an out-of-bounds page: {page}/{dividedLists.Length - 1}");
            player.SendPacket(new SCAuctionSearchedPacket(page, 0, [], (short)ErrorMessageType.NoErrorMessage, DateTime.UtcNow));
            return;
        }
        player.SendPacket(new SCAuctionSearchedPacket(page, dividedLists[page].Length, dividedLists[page].ToList(), (short)ErrorMessageType.NoErrorMessage, DateTime.UtcNow));
    }

    private AuctionLot GetCheapestAuctionLot(uint templateId)
    {
        var tempList = AuctionLots.Values.Where(lot => lot.Item.TemplateId == templateId).ToList();
        if (tempList.Count <= 0)
        {
            return null;
        }

        tempList = tempList.OrderBy(x => x.DirectMoney).ToList();

        return tempList.First();
    }

    public void CheapestAuctionLot(Character player, uint templateId, byte itemGrade = 0)
    {
        var directMoney = 0;
        var cheapestItem = GetCheapestAuctionLot(templateId);
        if (cheapestItem != null)
        {
            directMoney = cheapestItem.DirectMoney;
        }

        player.SendPacket(new SCAuctionLowestPricePacket(templateId, itemGrade, directMoney));
    }

    private void RemoveAuctionLot(AuctionLot itemToRemove)
    {
        if (!AuctionLots.ContainsKey(itemToRemove.Id))
        {
            return;
        }

        auctionIdManager.ReleaseId((uint)itemToRemove.Id);
        DeletedAuctionItemIds.Add((long)itemToRemove.Id);
        if (!AuctionLots.TryRemove(itemToRemove.Id, out _))
        {
            Logger.Warn($"Unable to remove Auction Lot with Id {itemToRemove.Id}");
        }
    }

    public void AddAuctionLot(AuctionLot lot)
    {
        if (!AuctionLots.TryAdd(lot.Id, lot))
        {
            Logger.Warn($"Unable to add Auction Lot with Id {lot.Id}, possible duplicate Id");
        }
    }

    public void UpdateAuctionHouse()
    {
        Logger.Trace("Updating Auction House!");
        var itemsToRemove = AuctionLots.Values.Where(c => DateTime.UtcNow > c.EndTime).ToList();

        foreach (var item in itemsToRemove)
        {
            if (item.BidderId != 0)
                RemoveAuctionLotSold(item, item.BidderName, item.BidMoney);
            else
                RemoveAuctionLotFail(item);
        }
    }

    public AuctionLot CreateAuctionLot(uint playerId, string playerName, Item itemToList, int startPrice, int buyoutPrice, AuctionDuration duration, int minStack = 1, int maxStack = 1)
    {
        ulong timeLeft;
        switch (duration)
        {
            case AuctionDuration.AuctionDuration6Hours:
                timeLeft = 6; // 6 hours
                break;
            case AuctionDuration.AuctionDuration12Hours:
                timeLeft = 12;
                break;
            case AuctionDuration.AuctionDuration24Hours:
                timeLeft = 24; // 24 hours
                break;
            case AuctionDuration.AuctionDuration48Hours:
                timeLeft = 48; // 48 hours
                break;
            default:
                timeLeft = 6; // default to 6 hours
                break;
        }

        var newAuctionLot = new AuctionLot
        {
            Id = auctionIdManager.GetNextId(), Duration = duration, Item = itemToList, EndTime = DateTime.UtcNow.AddHours(timeLeft),
            WorldId = 1,
            ClientId = playerId,
            ClientName = playerName,
            StartMoney = startPrice,
            DirectMoney = buyoutPrice,
            PostDate = DateTime.UtcNow,
            //ChargePercent = 100, // added in 5+
            BidWorldId = 255,
            BidderId = 0,
            BidderName = "",
            BidMoney = 0,
            Extra = 0,
            //MinStack = minStack, // added in 5+
            //MaxStack = maxStack, // added in 5+
            IsDirty = true
        };

        return newAuctionLot;
    }

    public void Load()
    {
        try
        {
            AuctionLots.Clear();
            DeletedAuctionItemIds.Clear();

            using (var connection = MySQL.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM auction_house";
                    command.Prepare();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var auctionLot = new AuctionLot
                            {
                                Id = reader.GetUInt64("id"),
                                Duration = (AuctionDuration)reader.GetByte("duration"), // 8 is 6 hours, 9 is 12 hours, 10 is 24 hours, 11 is 48 hours
                                Item = itemManager.GetItemByItemId(reader.GetUInt32("item_id")),
                                PostDate = reader.GetDateTime("post_date"),
                                EndTime = reader.GetDateTime("end_time"),
                                WorldId = reader.GetByte("world_id"),
                                ClientId = reader.GetUInt32("client_id"),
                                ClientName = reader.GetString("client_name"),
                                StartMoney = reader.GetInt32("start_money"),
                                DirectMoney = reader.GetInt32("direct_money"),
                                //ChargePercent = reader.GetInt32("charge_percent"), // added in 5+
                                BidWorldId = (byte)reader.GetInt32("bid_world_id"),
                                BidderId = reader.GetUInt32("bidder_id"),
                                BidderName = reader.GetString("bidder_name"),
                                BidMoney = reader.GetInt32("bid_money"),
                                Extra = reader.GetInt32("extra"),
                                //MinStack = reader.GetInt32("min_stack"), // added in 5+
                                //MaxStack = reader.GetInt32("max_stack") // added in 5+
                            };

                            AddAuctionLot(auctionLot);
                        }
                    }
                }
            }
            var auctionTask = new AuctionHouseTask();
            taskManager.Schedule(auctionTask, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load auction data: {ex.Message}");
        }
    }

    public (int, int) Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        var deletedCount = 0;
        var updatedCount = 0;

        if (!DeletedAuctionItemIds.IsEmpty)
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM auction_house WHERE `id` IN(" + string.Join(",", DeletedAuctionItemIds) + ")";
                command.Prepare();
                deletedCount = command.ExecuteNonQuery();
            }
            DeletedAuctionItemIds.Clear();
        }

        var dirtyItems = AuctionLots.Values.Where(c => c.IsDirty);
        foreach (var lot in dirtyItems)
        {
            if (lot.Item == null)
                continue;

            if (lot.Item.SlotType == SlotType.None)
            {
                if (lot.Item.OwnerId <= 0)
                    continue;

                if (lot.Item._holdingContainer != null)
                {
                    lot.Item.SlotType = itemManager.GetContainerSlotTypeByContainerId(lot.Item._holdingContainer.ContainerId);
                }

                if (lot.Item.SlotType != SlotType.None)
                {
                    Logger.Warn($"Slot type for {lot.Item.Id} was None, changing to {lot.Item.SlotType}");
                }
                else
                {
                    continue;
                }
            }
            if (!Enum.IsDefined(typeof(SlotType), lot.Item.SlotType))
            {
                Logger.Warn($"Found SlotType.{lot.Item.SlotType} in itemslist, skipping ID:{lot.Item.Id} - Template:{lot.Item.TemplateId}");
                continue;
            }

            var details = new Commons.Network.PacketStream();
            lot.Item.WriteDetails(details);

            using var command = connection.CreateCommand();
            command.Connection = connection;
            command.Transaction = transaction;
            command.CommandText = BuildInsertQuery();
            AddParametersToCommand(command, lot);
            command.Prepare();
            updatedCount += command.ExecuteNonQuery();
            lot.IsDirty = false;
        }

        return (updatedCount, deletedCount);
    }

    private string BuildInsertQuery()
    {
        var sb = new StringBuilder();
        sb.Append("REPLACE INTO auction_house(");
        sb.Append("`id`, `duration`, `item_id`, `post_date`, `stack_size`, `end_time`, ");
        sb.Append("`world_id`, `client_id`, `client_name`, `start_money`, `direct_money`, ");
        sb.Append("`bid_world_id`, `bidder_id`, `bidder_name`, `bid_money`, `extra`");
        sb.Append(") VALUES (");
        sb.Append("@id, @duration, @item_id, @post_date, @stack_size, @end_time, ");
        sb.Append("@world_id, @client_id, @client_name, @start_money, @direct_money, ");
        sb.Append("@bid_world_id, @bidder_id, @bidder_name, @bid_money, @extra");
        sb.Append(" )");

        return sb.ToString();
    }

    private void AddParametersToCommand(MySqlCommand command, AuctionLot lot)
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
        //command.Parameters.AddWithValue("@charge_percent", lot.ChargePercent); // added in 5+
        command.Parameters.AddWithValue("@bid_world_id", lot.BidWorldId);
        command.Parameters.AddWithValue("@bidder_id", lot.BidderId);
        command.Parameters.AddWithValue("@bidder_name", lot.BidderName);
        command.Parameters.AddWithValue("@bid_money", lot.BidMoney);
        command.Parameters.AddWithValue("@extra", lot.Extra);
        //command.Parameters.AddWithValue("@min_stack", lot.MinStack); // added in 5+
        //command.Parameters.AddWithValue("@max_stack", lot.MaxStack); // added in 5+
    }

    private List<AuctionLot> SortArticles(List<AuctionLot> articles, AuctionSearchSortKind kind, AuctionSearchSortOrder order)
    {
        var sortedArticles = articles.AsQueryable();

        switch (kind)
        {
            case AuctionSearchSortKind.BidPrice:
                sortedArticles = order == AuctionSearchSortOrder.Asc
                    ? sortedArticles.OrderBy(o => o.BidMoney)
                    : sortedArticles.OrderByDescending(o => o.BidMoney);
                break;
            case AuctionSearchSortKind.DirectPrice:
                sortedArticles = order == AuctionSearchSortOrder.Asc
                    ? sortedArticles.OrderBy(o => o.DirectMoney)
                    : sortedArticles.OrderByDescending(o => o.DirectMoney);
                break;
            case AuctionSearchSortKind.ExpireDate:
                sortedArticles = order == AuctionSearchSortOrder.Asc
                    ? sortedArticles.OrderBy(o => o.PostDate)
                    : sortedArticles.OrderByDescending(o => o.PostDate);
                break;
            case AuctionSearchSortKind.ItemLevel:
                sortedArticles = order == AuctionSearchSortOrder.Asc
                    ? sortedArticles.OrderBy(o => o.Item.Template.Level)
                    : sortedArticles.OrderByDescending(o => o.Item.Template.Level);
                break;
        }

        return sortedArticles.ToList();
    }

    public void SearchAuctionLots(Character player, AuctionSearch search)
    {
        var searchedArticles = new List<AuctionLot>();

        foreach (var (_, lot) in AuctionLots)
        {
            var template = lot.Item.Template;
            var settings = template.AuctionSettings;

            // Check by ClientId
            if (search.ClientId != 0 && lot.ClientId != search.ClientId)
            {
                continue;
            }

            // Check keyword
            if (!string.IsNullOrWhiteSpace(search.Keyword))
            {
                if (!localizationManager.MatchItemName(template.Id, search.Keyword, search.ExactMatch))
                    continue;
            }

            // Check by category and other criteria
            if (settings.CategoryA != search.CategoryA && search.CategoryA != 0)
            {
                continue;
            }

            if (settings.CategoryB != search.CategoryB && search.CategoryB != 0)
            {
                continue;
            }

            if (settings.CategoryC != search.CategoryC && search.CategoryC != 0)
            {
                continue;
            }

            if (lot.Item.Grade != search.Grade && search.Grade != 0)
            {
                continue;
            }

            if (template.Level > search.MaxItemLevel && search.MaxItemLevel != 0)
            {
                continue;
            }

            if (template.Level < search.MinItemLevel && search.MinItemLevel != 0)
            {
                continue;
            }

            searchedArticles.Add(lot);
        }

        if (searchedArticles.Count == 0)
        {
            player.SendPacket(new SCAuctionSearchedPacket(0, 0, [], (short)ErrorMessageType.NoErrorMessage, DateTime.UtcNow));
            return;
        }

        var articles = SortArticles(searchedArticles, search.SortKind, search.SortOrder).ToArray();
        var dividedLists = Helpers.SplitArray(articles, 9); // Разделяем массив на массивы по 9 значений

        if (search.Page < 0 || search.Page >= dividedLists.Length) // Stops client DC when requesting an out-of-bounds page
        {
            Logger.Warn($"[AH] {player.Name} requested an out-of-bounds page: {search.Page}/{dividedLists.Length - 1}");
            player.SendPacket(new SCAuctionSearchedPacket(search.Page, 0, [], (short)ErrorMessageType.NoErrorMessage, DateTime.UtcNow));
            return;
        }
        player.SendPacket(new SCAuctionSearchedPacket(search.Page, dividedLists[search.Page].Length, dividedLists[search.Page].ToList(), (short)ErrorMessageType.NoErrorMessage, DateTime.UtcNow));
    }

    public void PostLotOnAuction(Character player, uint npcId, uint npcId2, ulong itemId, int startPrice, int buyoutPrice, AuctionDuration duration)
    {
        var item = itemManager.GetItemByItemId(itemId);
        if (item == null)
        {
            return;
        }

        var lot = CreateAuctionLot(player.Id, player.Name, item, startPrice, buyoutPrice, duration);
        if (lot == null)
        {
            return;
        }

        var auctionFee = lot.DirectMoney * .01 * ((int)duration + 1);

        if (auctionFee > MaxListingFee)
        {
            auctionFee = MaxListingFee;
        }

        // Deduct AH fee
        if (!player.ChangeMoney(SlotType.Inventory, -(int)auctionFee))
        {
            player.SendErrorMessage(ErrorMessageType.CanNotPutupMoney);
            return;
        }

        player.Inventory.AuctionAttachments.AddOrMoveExistingItem(ItemTaskType.Auction, item);

        AddAuctionLot(lot);
        player.SendPacket(new SCAuctionPostedPacket(lot));
    }
}
