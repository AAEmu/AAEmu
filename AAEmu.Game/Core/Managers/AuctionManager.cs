using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Auction;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Scripts.Commands;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class AuctionManager : Singleton<AuctionManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public List<AuctionLot> AuctionLots;
    public Dictionary<(uint TemplateId, byte Grade), List<(DateTime Date, AuctionLot Lot)>> SalesData = new();
    public Dictionary<(uint TemplateId, byte Grade), List<(DateTime Date, AuctionSold Sold)>> SoldsData = new();

    public List<long> _deletedAuctionItemIds;

    private static int MaxListingFee = 1000000; // 100g, 100 copper coins = 1 silver, 100 silver = 1 gold.

    private void RemoveAuctionLotSold(AuctionLot itemToRemove, string buyer, int soldAmount)
    {
        if (AuctionLots.Contains(itemToRemove))
        {
            var newItem = ItemManager.Instance.GetItemByItemId(itemToRemove.Item.Id);

            var itemList = new Item[10].ToList();
            itemList[0] = newItem;

            var moneyAfterFee = soldAmount * .9;
            var moneyToSend = new int[3];
            moneyToSend[0] = (int)moneyAfterFee;

            // TODO: Read this from saved data
            var recalculatedFee = itemToRemove.DirectMoney * .01 * ((int)itemToRemove.Duration - 8 + 1);
            if (recalculatedFee > MaxListingFee) recalculatedFee = MaxListingFee;

            if (itemToRemove.ClientName != "")
            {
                var sellMail = new MailForAuction(newItem, itemToRemove.ClientId, soldAmount, (int)recalculatedFee);
                sellMail.FinalizeForSaleSeller((int)moneyAfterFee, (int)(soldAmount - moneyAfterFee));
                sellMail.Send();
            }

            var buyMail = new MailForAuction(newItem, itemToRemove.ClientId, soldAmount, (int)recalculatedFee);
            var buyerId = NameManager.Instance.GetCharacterId(buyer);
            buyMail.FinalizeForSaleBuyer(buyerId);
            buyMail.Send();

            RemoveAuctionLot(itemToRemove);
        }
    }

    private void BuyPartOfTheAuctionLot(AuctionLot auctionLot, string buyer, int soldAmount, int count)
    {
        // предмет должен быть с новым id
        var itemTemplate = ItemManager.Instance.GetItemTemplateFromItemId(auctionLot.Item.TemplateId);
        var newItem = ItemManager.Instance.Create(itemTemplate.Id, count, auctionLot.Item.Grade);

        var moneyAfterFee = soldAmount * .9;

        // TODO: Read this from saved data
        var recalculatedFee = auctionLot.DirectMoney * .01 * ((int)auctionLot.Duration - 8 + 1);
        if (recalculatedFee > MaxListingFee) recalculatedFee = MaxListingFee;

        if (auctionLot.ClientName != "")
        {
            // Хозяину отсылаем часть денег за покупку
            var sellMail = new MailForAuction(newItem, auctionLot.ClientId, soldAmount, (int)recalculatedFee);
            sellMail.FinalizeForSaleSeller((int)moneyAfterFee, (int)(soldAmount - moneyAfterFee));
            sellMail.Send();
        }

        // Покупателю отсылаем купленный лот
        var buyMail = new MailForAuction(newItem, auctionLot.ClientId, soldAmount, (int)recalculatedFee);
        var buyerId = NameManager.Instance.GetCharacterId(buyer);
        buyMail.FinalizeForSaleBuyer(buyerId);
        buyMail.Send();

        RemoveAuctionLot(auctionLot);
    }

    private void RemoveAuctionLotFail(AuctionLot itemToRemove)
    {
        if (!AuctionLots.Contains(itemToRemove))
            return;

        if (itemToRemove.BidderName != "") // Player won the bid.
        {
            RemoveAuctionLotSold(itemToRemove, itemToRemove.BidderName, itemToRemove.BidMoney);
            return;
        }

        // Item did not sell by end of the timer.
        var newItem = ItemManager.Instance.GetItemByItemId(itemToRemove.Item.Id);
        var itemList = new Item[10].ToList();
        itemList[0] = newItem;

        // TODO: Read this from saved data
        var recalculatedFee = itemToRemove.DirectMoney * .01 * ((int)itemToRemove.Duration - 8 + 1);
        if (recalculatedFee > MaxListingFee) recalculatedFee = MaxListingFee;

        if (itemToRemove.ClientName != "")
        {
            var failMail = new MailForAuction(newItem, itemToRemove.ClientId, itemToRemove.DirectMoney, (int)recalculatedFee);
            failMail.FinalizeForFail();
            failMail.Send();
        }

        RemoveAuctionLot(itemToRemove);
    }

    public void CancelAuctionLot(Character player, ulong auctionId)
    {
        var auctionItem = GetAuctionLotFromId(auctionId);

        if (auctionItem.BidderName != "") return;// Someone has already bid on the item and we do not want to remove it. 

        var moneyToSubtract = auctionItem.DirectMoney * .1f;
        var itemList = new Item[10].ToList();
        var newItem = ItemManager.Instance.Create(auctionItem.Item.TemplateId, auctionItem.Item.Count, auctionItem.Item.Grade);
        itemList[0] = newItem;

        // TODO: Read this from saved data
        var recalculatedFee = auctionItem.DirectMoney * .01 * ((int)auctionItem.Duration - 8 + 1);
        if (recalculatedFee > MaxListingFee) recalculatedFee = MaxListingFee;

        var cancelMail = new MailForAuction(newItem, auctionItem.ClientId, auctionItem.DirectMoney, (int)recalculatedFee);
        cancelMail.FinalizeForCancel();
        cancelMail.Send();
        //MailManager.Instance.SendMail(0, auctionItem.ClientName, "AuctionHouse", "Cancelled Listing", "See attached.", 1, new int[3], 0, itemList);

        RemoveAuctionLot(auctionItem);
        player.SendPacket(new SCAuctionCanceledPacket(auctionItem));
    }

    private AuctionLot GetAuctionLotFromId(ulong auctionId)
    {
        var lot = AuctionLots.Single(c => c.Id == auctionId);

        return lot;
    }

    public void BidOnAuctionLot(Character player, uint auctioneerId, uint auctioneerId2, AuctionLot lot, AuctionBid bid)
    {
        if (player == null || lot == null || bid == null)
        {
            // Логирование или обработка ошибки
            Logger.Warn("Invalid arguments passed to BidOnAuctionLot.");
            return;
        }

        //var auctionItem = GetAuctionLotFromId(lot.Id);
        var auctionLot = lot;
        if (auctionLot != null)
        {
            // TODO сравним, что прислал клиент и что есть на сервере
            // auctionItem == lot ?

            if (bid.StackSize != 0 && bid.StackSize >= auctionLot.MinStack && bid.StackSize <= auctionLot.MaxStack && auctionLot.BidderId == 0) // Buy part of the lot
            {
                BuyPartOfTheAuctionLot(auctionLot, player.Name, bid.Money, bid.StackSize);

                // Set info to new bidders info
                auctionLot.Item.Count -= bid.StackSize;

                bid.BidderName = player.Name;
                bid.BidderId = player.Id;
                bid.WorldId = (byte)player.Transform.WorldId;

                player.SubtractMoney(SlotType.Bag, bid.Money);
                player.SendPacket(new SCAuctionBidPacket(bid, false, auctionLot.Item.TemplateId));
                auctionLot.IsDirty = true;

                // Обновление данных в списке AuctionLots
                UpdateAuctionLotInList(auctionLot);

                auctionLot.BidMoney = bid.Money;
                auctionLot.Extra = bid.StackSize;
            }
            else if (bid.Money >= auctionLot.DirectMoney && auctionLot.DirectMoney != 0) // Buy now
            {
                if (auctionLot.BidderId != 0) // send mail to person who bid if item was bought at full price.
                {
                    var newMail = new MailForAuction(auctionLot.Item.TemplateId, auctionLot.ClientId, auctionLot.DirectMoney, 0);
                    newMail.FinalizeForBidFail(auctionLot.BidderId, auctionLot.BidMoney);
                    newMail.Send();
                }

                player.SubtractMoney(SlotType.Bag, auctionLot.DirectMoney);
                RemoveAuctionLotSold(auctionLot, player.Name, auctionLot.DirectMoney);

                auctionLot.BidMoney = bid.Money;
                auctionLot.Extra = bid.StackSize;
            }
            else if (bid.Money > auctionLot.BidMoney) // Bid
            {
                if (auctionLot.BidderName != "" && auctionLot.BidderId != 0) // Send mail to old bidder.
                {
                    var moneyArray = new int[3];
                    moneyArray[0] = auctionLot.BidMoney;

                    // TODO: Read this from saved data
                    var recalculatedFee = auctionLot.DirectMoney * .01 * ((int)auctionLot.Duration - 8 + 1);
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

                player.SubtractMoney(SlotType.Bag, bid.Money, ItemTaskType.Auction);
                player.SendPacket(new SCAuctionBidPacket(bid, false, auctionLot.Item.TemplateId));
                auctionLot.IsDirty = true;

                // Обновление данных в списке AuctionLots
                UpdateAuctionLotInList(auctionLot);

                auctionLot.BidMoney = bid.Money;
                auctionLot.Extra = bid.StackSize;
            }

            AddAuctionSold(auctionLot);
        }
    }

    public void UpdateAuctionLotInList(AuctionLot auctionLot)
    {
        if (auctionLot == null)
        {
            // Логирование или обработка ошибки
            Logger.Warn("Invalid auctionItem passed to UpdateAuctionLotInList.");
            return;
        }

        lock (AuctionLots)
        {
            // Поиск лота в списке по идентификатору
            var existingLot = AuctionLots.FirstOrDefault(lot => lot.Id == auctionLot.Id);

            if (existingLot != null)
            {
                // Обновление данных лота
                existingLot.BidderName = auctionLot.BidderName;
                existingLot.BidderId = auctionLot.BidderId;
                existingLot.BidWorldId = auctionLot.BidWorldId;
                existingLot.BidMoney = auctionLot.BidMoney;
                existingLot.Item.Count = auctionLot.Item.Count;
                existingLot.IsDirty = auctionLot.IsDirty;
            }
            else
            {
                // Логирование или обработка ошибки, если лот не найден
                Logger.Warn($"AuctionLot with ID {auctionLot.Id} not found in the list.");
            }
        }
    }

    public void GetBidAuctionLots(Character player, int page)
    {
        var searchedArticles = AuctionLots.Where(lot => lot.BidderId == player.Id).ToList();

        if (searchedArticles.Count <= 0)
        {
            player.SendPacket(new SCAuctionSearchedPacket(0, 0, [], (short)ErrorMessageType.NoErrorMessage, DateTime.UtcNow));
            return;
        }

        var articles = SortArticles(searchedArticles, AuctionSearchSortKind.Default, AuctionSearchSortOrder.Asc).ToArray();
        var dividedLists = Helpers.SplitArray(articles, 9); // Разделяем массив на массивы по 9 значений
        player.SendPacket(new SCAuctionSearchedPacket(page, dividedLists[page].Length, dividedLists[page].ToList(), (short)ErrorMessageType.NoErrorMessage, DateTime.UtcNow));
    }

    public AuctionLot GetCheapestAuctionLot(uint templateId)
    {
        var tempList = AuctionLots.Where(lot => lot.Item.TemplateId == templateId).ToList();

        if (tempList.Count <= 0)
        {
            return null;
        }

        tempList = tempList.OrderBy(x => x.DirectMoney).ToList();

        return tempList.First();
    }

    public void CheapestAuctionLot(Character player, uint templateId, byte itemGrade = 0)
    {
        var DirectMoney = 0;
        var cheapestItem = GetCheapestAuctionLot(templateId);
        if (cheapestItem != null)
        {
            DirectMoney = cheapestItem.DirectMoney;
        }

        player.SendPacket(new SCAuctionLowestPricePacket(templateId, itemGrade, DirectMoney));
    }

    public static string GetLocalizedItemNameById(uint id)
    {
        return LocalizationManager.Instance.Get("items", "name", id, ItemManager.Instance.GetTemplate(id).Name ?? "");
    }

    public ulong GetNextId()
    {
        ulong nextId = 0;
        foreach (var item in AuctionLots)
        {
            if (nextId < item.Id)
                nextId = item.Id;
        }
        return nextId + 1;
    }

    public void RemoveAuctionLot(AuctionLot itemToRemove)
    {
        //if (itemToRemove.ClientName == "") // Testing feature. Relists an item if the server listed it. 
        //{
        //    itemToRemove.EndTime = DateTime.UtcNow.AddHours(48);
        //    return;
        //}
        lock (AuctionLots)
        {
            lock (_deletedAuctionItemIds)
            {
                if (AuctionLots.Contains(itemToRemove))
                {
                    _deletedAuctionItemIds.Add((long)itemToRemove.Id);
                    AuctionLots.Remove(itemToRemove);
                }
            }
        }
    }

    public void AddAuctionLot(AuctionLot lot)
    {
        lock (AuctionLots)
        {
            AuctionLots.Add(lot);
        }
    }

    public void UpdateAuctionHouse()
    {
        Logger.Trace("Updating Auction House!");
        lock (AuctionLots)
        {
            var itemsToRemove = AuctionLots.Where(c => DateTime.UtcNow > c.EndTime);

            foreach (var item in itemsToRemove)
            {
                if (item.BidderId != 0)
                    RemoveAuctionLotSold(item, item.BidderName, item.BidMoney);
                else
                    RemoveAuctionLotFail(item);
            }
        }
    }

    public AuctionLot CreateAuctionLot(Character player, Item itemToList, int startPrice, int buyoutPrice, AuctionDuration duration, int minStack, int maxStack)
    {
        ulong timeLeft;
        switch (duration)
        {
            case AuctionDuration.AuctionDuration6Hours:
                timeLeft = 6; // 6 hours
                break;
            case AuctionDuration.AuctionDuration12Hours:
                timeLeft = 12; // 12 hours
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

        var newAuctionLot = new AuctionLot();
        newAuctionLot.Id = GetNextId();
        newAuctionLot.Duration = duration;

        newAuctionLot.Item = itemToList;

        newAuctionLot.EndTime = DateTime.UtcNow.AddHours(timeLeft);

        newAuctionLot.WorldId = 1;
        newAuctionLot.ClientId = player.Id;
        newAuctionLot.ClientName = player.Name;
        newAuctionLot.StartMoney = startPrice;
        newAuctionLot.DirectMoney = buyoutPrice;
        newAuctionLot.PostDate = DateTime.UtcNow;
        newAuctionLot.ChargePercent = 1000; // added in 5+
        newAuctionLot.BidWorldId = 255;
        newAuctionLot.BidderId = 0;
        newAuctionLot.BidderName = "";
        newAuctionLot.BidMoney = 0;
        newAuctionLot.Extra = 0;
        newAuctionLot.MinStack = minStack; // added in 5+
        newAuctionLot.MaxStack = maxStack; // added in 5+
        newAuctionLot.IsDirty = true;

        return newAuctionLot;
    }

    public void Load()
    {
        AuctionLots = [];
        SoldsData = [];
        _deletedAuctionItemIds = [];

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
                        var auctionLot = new AuctionLot();
                        auctionLot.Id = reader.GetUInt64("id");
                        auctionLot.Duration = (AuctionDuration)reader.GetByte("duration"); // 8 is 6 hours, 9 is 12 hours, 10 is 24 hours, 11 is 48 hours

                        var itemId = reader.GetUInt32("item_id");
                        var item = ItemManager.Instance.GetItemByItemId(itemId);
                        if (item == null)
                        {
                            continue;
                        }

                        auctionLot.EndTime = reader.GetDateTime("end_time");

                        auctionLot.WorldId = reader.GetByte("world_id");
                        auctionLot.ClientId = reader.GetUInt32("client_id");
                        auctionLot.ClientName = reader.GetString("client_name");
                        auctionLot.StartMoney = reader.GetInt32("start_money");
                        auctionLot.DirectMoney = reader.GetInt32("direct_money");

                        auctionLot.ChargePercent = reader.GetInt32("charge_percent"); // added in 5+

                        auctionLot.BidWorldId = (byte)reader.GetInt32("bid_world_id");
                        auctionLot.BidderId = reader.GetUInt32("bidder_id");
                        auctionLot.BidderName = reader.GetString("bidder_name");
                        auctionLot.BidMoney = reader.GetInt32("bid_money");
                        auctionLot.Extra = reader.GetInt32("extra");

                        auctionLot.MinStack = reader.GetInt32("min_stack"); // added in 5+
                        auctionLot.MaxStack = reader.GetInt32("max_stack"); // added in 5+

                        AddAuctionLot(auctionLot);
                    }
                }
            }

            ReadAuctionSoldsData(connection);
        }
        var auctionTask = new AuctionHouseTask();
        TaskManager.Instance.Schedule(auctionTask, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public (int, int) Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        var deletedCount = 0;
        var updatedCount = 0;

        lock (_deletedAuctionItemIds)
        {
            deletedCount = _deletedAuctionItemIds.Count;
            if (_deletedAuctionItemIds.Count > 0)
            {
                using (var command = connection.CreateCommand())
                {
                    command.Connection = connection;
                    command.Transaction = transaction;
                    command.CommandText = "DELETE FROM auction_house WHERE `id` IN(" + string.Join(",", _deletedAuctionItemIds) + ")";
                    command.Prepare();
                    command.ExecuteNonQuery();
                }
                _deletedAuctionItemIds.Clear();
            }
        }

        var dirtyItems = AuctionLots.Where(c => c.IsDirty == true);
        foreach (var mtbs in dirtyItems)
        {
            if (mtbs.Item == null)
                continue;

            if (mtbs.Item.SlotType == SlotType.Invalid)
            {
                // Only give an error if it has no owner, otherwise it's likely a BuyBack item
                if (mtbs.Item.OwnerId <= 0)
                    continue;

                // Try to re-attain the slot type by getting the owning container's type
                if (mtbs.Item._holdingContainer != null)
                {
                    mtbs.Item.SlotType = ItemManager.Instance.GetContainerSlotTypeByContainerId(mtbs.Item._holdingContainer.ContainerId);
                }

                // If the slot type changed, give a warning, otherwise skip this save
                if (mtbs.Item.SlotType != SlotType.Invalid)
                {
                    Logger.Warn($"Slot type for {mtbs.Item.Id} was None, changing to {mtbs.Item.SlotType}");
                }
                else
                {
                    continue;
                }
            }
            if (!Enum.IsDefined(typeof(SlotType), mtbs.Item.SlotType))
            {
                Logger.Warn($"Found SlotType.{mtbs.Item.SlotType} in itemslist, skipping ID:{mtbs.Item.Id} - Template:{mtbs.Item.TemplateId}");
                continue;
            }

            var details = new Commons.Network.PacketStream();
            mtbs.Item.WriteDetails(details);

            using (var command = connection.CreateCommand())
            {
                // TODO отключим сохранение в базу

                command.Connection = connection;
                command.Transaction = transaction;

                command.CommandText = "REPLACE INTO auction_house(" +
                    "`id`, `duration`," +
                    " `item_id`," +
                    "`end_time`," +
                    " `world_id`, `client_id`, `client_name`," +
                    " `start_money`, `direct_money`, `charge_percent`," +
                    " `bid_world_id`, `bidder_id`, `bidder_name`," +
                    " `bid_money`, `extra`, `min_stack`, `max_stack`" +
                    ") VALUES (" +
                    "@id, @duration," +
                    " @item_id," +
                    "@end_time," +
                    " @world_id, @client_id, @client_name," +
                    " @start_money, @direct_money, @charge_percent," +
                    " @bid_world_id, @bidder_id, @bidder_name," +
                    " @bid_money, @extra, @min_stack, @max_stack)";

                command.Parameters.AddWithValue("@id", mtbs.Id);
                command.Parameters.AddWithValue("@duration", (byte)mtbs.Duration);

                command.Parameters.AddWithValue("@item_id", mtbs.Item.Id); // item.Id

                command.Parameters.AddWithValue("@end_time", mtbs.EndTime);
                command.Parameters.AddWithValue("@world_id", mtbs.WorldId);
                command.Parameters.AddWithValue("@client_id", mtbs.ClientId);
                command.Parameters.AddWithValue("@client_name", mtbs.ClientName);
                command.Parameters.AddWithValue("@start_money", mtbs.StartMoney);
                command.Parameters.AddWithValue("@direct_money", mtbs.DirectMoney);
                command.Parameters.AddWithValue("@charge_percent", mtbs.ChargePercent); // added in 5+
                command.Parameters.AddWithValue("@bid_world_id", mtbs.BidWorldId);
                command.Parameters.AddWithValue("@bidder_id", mtbs.BidderId);
                command.Parameters.AddWithValue("@bidder_name", mtbs.BidderName);
                command.Parameters.AddWithValue("@bid_money", mtbs.BidMoney);
                command.Parameters.AddWithValue("@extra", mtbs.Extra);
                command.Parameters.AddWithValue("@min_stack", mtbs.MinStack); // added in 5+
                command.Parameters.AddWithValue("@max_stack", mtbs.MaxStack); // added in 5+

                command.Prepare();
                command.ExecuteNonQuery();
                updatedCount++;
                mtbs.IsDirty = false;
            }
        }

        SaveSoldsData(connection);

        return (updatedCount, deletedCount);
    }

    private List<AuctionLot> SortArticles(List<AuctionLot> articles, AuctionSearchSortKind kind, AuctionSearchSortOrder order)
    {
        if (kind == AuctionSearchSortKind.BidPrice)
        {
            if (order == AuctionSearchSortOrder.Asc)
            {
                return articles.OrderBy(o => o.BidMoney).ToList();
            }

            return articles.OrderByDescending(o => o.BidMoney).ToList();
        }

        if (kind == AuctionSearchSortKind.DirectPrice)
        {
            if (order == AuctionSearchSortOrder.Asc)
            {
                return articles.OrderBy(o => o.DirectMoney).ToList();
            }

            return articles.OrderByDescending(o => o.DirectMoney).ToList();
        }

        if (kind == AuctionSearchSortKind.ExpireDate) // TODO This will need to be fixed later due to varying durations
        {
            if (order == AuctionSearchSortOrder.Asc)
            {
                return articles.OrderBy(o => o.PostDate).ToList();
            }

            return articles.OrderByDescending(o => o.PostDate).ToList();
        }

        if (kind == AuctionSearchSortKind.ItemLevel)
        {
            if (order == AuctionSearchSortOrder.Asc)
            {
                return articles.OrderBy(o => o.Item.Template.Level).ToList();
            }

            return articles.OrderByDescending(o => o.Item.Template.Level).ToList();
        }

        return articles;
    }

    public void SearchAuctionLots(Character player, AuctionSearch search)
    {
        var searchedArticles = new List<AuctionLot>();

        var detectedLanguage = LanguageDetector.DetectLanguage(search.Keyword);
        Logger.Info($"Detected language for keyword '{search.Keyword}': {detectedLanguage}");

        foreach (var lot in AuctionLots)
        {
            var template = lot.Item.Template;
            var settings = template.AuctionSettings;

            template.Name = GetLocalizedItemNameById(template.Id);
            // Проверка по ClientId
            if (search.ClientId != 0 && lot.ClientId != search.ClientId)
            {
                continue;
            }

            // Проверка по ключевому слову
            if (!string.IsNullOrEmpty(search.Keyword))
            {
                var itemName = template.Name.ToLower();
                var keyword = search.Keyword.ToLower();

                if (search.ExactMatch)
                {
                    if (itemName != keyword)
                    {
                        continue;
                    }
                }
                else
                {
                    if (!itemName.Contains(keyword))
                    {
                        continue;
                    }
                }
            }
            else
            {
                // Проверка по категориям и другим параметрам
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
        player.SendPacket(new SCAuctionSearchedPacket(search.Page, dividedLists[search.Page].Length, dividedLists[search.Page].ToList(), (short)ErrorMessageType.NoErrorMessage, DateTime.UtcNow));
    }

    public void PostLotOnAuction(Character player, uint npcId, uint npcId2, ulong itemId, int startPrice, int buyoutPrice, AuctionDuration duration, int minStack, int maxStack)
    {
        var item = ItemManager.Instance.GetItemByItemId(itemId);
        if (item == null)
        {
            return;
        }

        var lot = CreateAuctionLot(player, item, startPrice, buyoutPrice, duration, minStack, maxStack);
        if (lot == null)
        {
            return;
        }

        var auctionFee = lot.DirectMoney * .01 * ((int)duration - 8 + 1);

        if (auctionFee > MaxListingFee)
        {
            auctionFee = MaxListingFee;
        }

        if (!player.ChangeMoney(SlotType.Bag, -(int)auctionFee))
        {
            player.SendErrorMessage(ErrorMessageType.CanNotPutupMoney);
            return;
        }

        player.Inventory.AuctionAttachments.AddOrMoveExistingItem(ItemTaskType.Auction, item);

        AddAuctionLot(lot);
        player.SendPacket(new SCAuctionPostedPacket(lot));
    }

    private class LanguageDetector
    {
        private static readonly string[] CyrillicLanguages = ["ru", "uk", "bg", "sr", "mk"];
        private static readonly string[] LatinLanguages = ["en", "es", "fr", "de", "it"];

        public static string DetectLanguage(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "unknown";
            }

            // Проверка на кириллицу
            if (text.Any(c => IsCyrillic(c)))
            {
                return "ru"; // Предполагаем русский язык, если есть кириллические символы
            }

            // Проверка на латиницу
            if (text.Any(c => IsLatin(c)))
            {
                return "en"; // Предполагаем английский язык, если есть латинские символы
            }

            return "unknown";
        }

        private static bool IsCyrillic(char c)
        {
            return (c >= '\u0400' && c <= '\u04FF') || (c >= '\u0500' && c <= '\u052F');
        }

        private static bool IsLatin(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        }
    }

    private void AddAuctionSold(AuctionLot lot)
    {
        var key = (lot.Item.TemplateId, lot.Item.Grade);
        if (!SalesData.ContainsKey(key))
        {
            SalesData[key] = new List<(DateTime, AuctionLot)>();
        }
        SalesData[key].Add((DateTime.UtcNow, lot));
    }

    public List<AuctionSold> GetSalesForLast14Days(uint itemTemplateId, byte itemGradeId)
    {
        var key = (itemTemplateId, itemGradeId);
        var result = new List<AuctionSold>();

        // Создаем список из 14 дней с нулевыми значениями
        for (var i = 0; i < 14; i++)
        {
            result.Add(new AuctionSold
            {
                ItemId = itemTemplateId,
                Day = i + 1,
                MinCopper = 0,
                MaxCopper = 0,
                AvgCopper = 0,
                Volume = 0,
                ItemGrade = itemGradeId,
                WeeklyAvgCopper = 0
            });
        }

        if (SalesData.Count != 0)
        {
            var sales = SalesData[key];
            var salesByDay = sales.GroupBy(s => s.Date.Date).OrderBy(g => g.Key).TakeLast(14);

            foreach (var daySales in salesByDay)
            {
                var day = (daySales.Key - DateTime.UtcNow.Date).Days + 1;
                var salesForDay = daySales.ToList();

                if (salesForDay.Count > 0)
                {
                    var minCopper = salesForDay.Min(s => s.Lot.BidMoney);
                    var maxCopper = salesForDay.Max(s => s.Lot.BidMoney);
                    var avgCopper = (long)salesForDay.Average(s => s.Lot.BidMoney);
                    var volume = salesForDay.Sum(s => s.Lot.Extra);

                    // Расчет WeeklyAvgCopper
                    var weeklySales = salesByDay.Where(g => (g.Key - daySales.Key).Days >= 0 && (g.Key - daySales.Key).Days < 7).SelectMany(g => g);
                    var weeklyAvgCopper = weeklySales.Any() ? (long)weeklySales.Average(s => s.Lot.BidMoney) : 0;

                    result[14 - day] = new AuctionSold
                    {
                        ItemId = itemTemplateId,
                        Day = day,
                        MinCopper = minCopper,
                        MaxCopper = maxCopper,
                        AvgCopper = avgCopper,
                        Volume = volume,
                        ItemGrade = itemGradeId,
                        WeeklyAvgCopper = weeklyAvgCopper
                    };
                }
            }

            // Сохраняем результат в SoldsData
            if (!SoldsData.ContainsKey(key))
            {
                SoldsData[key] = [];
            }

            for (var i = 0; i < 14; i++)
            {
                var date = DateTime.UtcNow.Date.AddDays(-i);
                var sold = result[14 - i - 1];
                SoldsData[key].Add((date, sold));
            }
        }
        else
        {
            var res = GetLast14AuctionSoldByItemId(MySQL.CreateConnection(), key);
            if (res.Count > 0)
            {
                result = res;
            }
        }

        return result;
    }

    private List<AuctionSold> GetLast14AuctionSoldByItemId(MySqlConnection connection, (uint itemTemplateId, byte itemGradeId) key)
    {

        //public static List<(DateTime Date, AuctionSold Sold)> GetLast14AuctionSoldByItemId(MySqlConnection connection, (uint itemTemplateId, byte itemGradeId) key)

        // Список для хранения данных
        var last14AuctionSold = new List<AuctionSold>();

        // SQL-запрос для выборки данных с использованием ROW_NUMBER()
        var query = @"
            WITH RankedData AS (
                SELECT 
                    item_id, 
                    item_grade, 
                    date, 
                    min_copper, 
                    max_copper, 
                    avg_copper, 
                    volume, 
                    weekly_avg_copper,
                    ROW_NUMBER() OVER (PARTITION BY item_id, item_grade ORDER BY date ASC) AS rn
                FROM 
                    auction_solds_data
                WHERE 
                    item_id = @itemTemplateId AND item_grade = @itemGradeId
            )
            SELECT 
                item_id, 
                item_grade, 
                date, 
                min_copper, 
                max_copper, 
                avg_copper, 
                volume, 
                weekly_avg_copper,
                rn
            FROM 
                RankedData
            WHERE 
                rn <= 14";

        var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@itemTemplateId", key.itemTemplateId);
        command.Parameters.AddWithValue("@itemGradeId", key.itemGradeId);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var itemId = reader.GetUInt32("item_id");
            var itemGrade = reader.GetByte("item_grade");
            var date = reader.GetDateTime("date");
            var minCopper = reader.GetInt64("min_copper");
            var maxCopper = reader.GetInt64("max_copper");
            var avgCopper = reader.GetInt64("avg_copper");
            var volume = reader.GetInt32("volume");
            var weeklyAvgCopper = reader.GetInt64("weekly_avg_copper");
            var day = reader.GetInt32("rn"); // Номер строки (ранг)

            // Создаем объект AuctionSold
            var sold = new AuctionSold
            {
                ItemId = itemId,
                ItemGrade = itemGrade,
                MinCopper = minCopper,
                MaxCopper = maxCopper,
                AvgCopper = avgCopper,
                Volume = volume,
                WeeklyAvgCopper = weeklyAvgCopper,
                Day = day
            };

            // Добавляем данные в список
            last14AuctionSold.Add(sold);
        }

        return last14AuctionSold;
    }

    private void ReadAuctionSoldsData(MySqlConnection connection)
    {
        // SQL-запрос для выборки данных с использованием ROW_NUMBER()
        var query = @"
                WITH RankedData AS (
                    SELECT 
                        item_id, 
                        item_grade, 
                        date, 
                        min_copper, 
                        max_copper, 
                        avg_copper, 
                        volume, 
                        weekly_avg_copper,
                        ROW_NUMBER() OVER (PARTITION BY item_id, item_grade ORDER BY date ASC) AS rn
                    FROM 
                        auction_solds_data
                )
                SELECT 
                    item_id, 
                    item_grade, 
                    date, 
                    min_copper, 
                    max_copper, 
                    avg_copper, 
                    volume, 
                    weekly_avg_copper,
                    rn
                FROM 
                    RankedData
                WHERE 
                    rn <= 14";

        var command = new MySqlCommand(query, connection);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var itemId = reader.GetUInt32("item_id");
            var itemGrade = reader.GetByte("item_grade");
            var date = reader.GetDateTime("date");
            var minCopper = reader.GetInt64("min_copper");
            var maxCopper = reader.GetInt64("max_copper");
            var avgCopper = reader.GetInt64("avg_copper");
            var volume = reader.GetInt32("volume");
            var weeklyAvgCopper = reader.GetInt64("weekly_avg_copper");
            var day = reader.GetInt32("rn"); // Номер строки (ранг)

            // Создаем объект AuctionSold
            var sold = new AuctionSold
            {
                ItemId = itemId,
                ItemGrade = itemGrade,
                MinCopper = minCopper,
                MaxCopper = maxCopper,
                AvgCopper = avgCopper,
                Volume = volume,
                WeeklyAvgCopper = weeklyAvgCopper,
                Day = day
            };

            // Добавляем данные в словарь
            var key = (itemId, itemGrade);
            if (!SoldsData.ContainsKey(key))
            {
                SoldsData[key] = new List<(DateTime, AuctionSold)>();
            }
            SoldsData[key].Add((date, sold));
        }
    }

    private void SaveSoldsData(MySqlConnection connection)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        try
        {
            foreach (var (key, solds) in SoldsData)
            {
                foreach (var (date, sold) in solds)
                {
                    SaveAuctionSold(connection, sold, date);
                }
            }

        }
        catch (Exception ex)
        {
            throw new Exception("Failed to save solds data to the database.", ex);
        }
    }

    private static void SaveAuctionSold(MySqlConnection connection, AuctionSold auctionSold, DateTime date)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO auction_solds_data (item_id, item_grade, date, min_copper, max_copper, avg_copper, volume, weekly_avg_copper) " +
                              "VALUES (@item_id, @item_grade, @date, @min_copper, @max_copper, @avg_copper, @volume, @weekly_avg_copper) " +
                              "ON DUPLICATE KEY UPDATE min_copper = @min_copper, max_copper = @max_copper, avg_copper = @avg_copper, volume = @volume, weekly_avg_copper = @weekly_avg_copper;";

        command.Parameters.AddWithValue("@item_id", auctionSold.ItemId);
        command.Parameters.AddWithValue("@item_grade", auctionSold.ItemGrade);
        command.Parameters.AddWithValue("@date", date);
        command.Parameters.AddWithValue("@min_copper", auctionSold.MinCopper);
        command.Parameters.AddWithValue("@max_copper", auctionSold.MaxCopper);
        command.Parameters.AddWithValue("@avg_copper", auctionSold.AvgCopper);
        command.Parameters.AddWithValue("@volume", auctionSold.Volume);
        command.Parameters.AddWithValue("@weekly_avg_copper", auctionSold.WeeklyAvgCopper);

        command.Prepare();
        command.ExecuteNonQuery();
    }
}
