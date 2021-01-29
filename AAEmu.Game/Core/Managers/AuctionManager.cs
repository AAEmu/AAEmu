using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Auction;
using AAEmu.Game.Models.Game.Auction.Templates;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.DB;
using AAEmu.Game.Models.Game.Items;
using NLog;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items.Templates;
using MySql.Data.MySqlClient;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Managers
{
    public class AuctionManager : Singleton<AuctionManager>
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();

        public List<AuctionItem> _auctionItems;
        public List<long> _deletedAuctionItemIds;

        private static int MaxListingFee = 1000000; // 100g

        public void ListAuctionItem(Character player, ulong itemId, int startPrice, int buyoutPrice, byte duration)
        {
            var newItem = player.Inventory.GetItemById(itemId);
            var newAuctionItem = CreateAuctionItem(player, newItem, startPrice, buyoutPrice, duration);

            if (newItem == null || newAuctionItem == null)
            {
                player.SendErrorMessage(Models.Game.Error.ErrorMessageType.AucInternalError);
                return;
            }

            var auctionFee = (newAuctionItem.DirectMoney * .01) * (duration + 1);

            if (auctionFee > MaxListingFee)
                auctionFee = MaxListingFee;

            if (!player.ChangeMoney(SlotType.Inventory, -(int)auctionFee))
            {
                player.SendErrorMessage(Models.Game.Error.ErrorMessageType.CanNotPutupMoney);
                return;
            }
            player.Inventory.Bag.RemoveItem(Models.Game.Items.Actions.ItemTaskType.Auction, newItem, true);
            AddAuctionItem(newAuctionItem);
            player.SendPacket(new SCAuctionPostedPacket(newAuctionItem));
        }

        private void RemoveAuctionItemSold(AuctionItem itemToRemove, string buyer, int soldAmount)
        {
            if (_auctionItems.Contains(itemToRemove))
            {
                var itemTemplate = ItemManager.Instance.GetItemTemplateFromItemId(itemToRemove.ItemID);
                var newItem = ItemManager.Instance.Create(itemTemplate.Id, (int)itemToRemove.StackSize, itemToRemove.Grade);
                var itemList = new Item[10].ToList();
                itemList[0] = newItem;

                var moneyAfterFee = soldAmount * .9;
                var moneyToSend = new int[3];
                moneyToSend[0] = (int)moneyAfterFee;

                // TODO: Read this from saved data
                var recalculatedFee = (itemToRemove.DirectMoney * .01) * (itemToRemove.Duration + 1);
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

                RemoveAuctionItem(itemToRemove);
            }
        }

        private void RemoveAuctionItemFail(AuctionItem itemToRemove)
        {
            if (!_auctionItems.Contains(itemToRemove))
                return;
            
            if (itemToRemove.BidderName != "") //Player won the bid. 
            {
                RemoveAuctionItemSold(itemToRemove, itemToRemove.BidderName, itemToRemove.BidMoney);
                return;
            }
            else //Item did not sell by end of the timer. 
            {
                var itemTemplate = ItemManager.Instance.GetItemTemplateFromItemId(itemToRemove.ItemID);
                var newItem = ItemManager.Instance.Create(itemTemplate.Id, (int)itemToRemove.StackSize, itemToRemove.Grade);
                var itemList = new Item[10].ToList();
                itemList[0] = newItem;

                // TODO: Read this from saved data
                var recalculatedFee = (itemToRemove.DirectMoney * .01) * (itemToRemove.Duration + 1);
                if (recalculatedFee > MaxListingFee) recalculatedFee = MaxListingFee;

                if (itemToRemove.ClientName != "")
                {
                    var failMail = new MailForAuction(newItem, itemToRemove.ClientId, itemToRemove.DirectMoney, (int)recalculatedFee);
                    failMail.FinalizeForFail();
                    failMail.Send();   
                }

                RemoveAuctionItem(itemToRemove);
            }
        }

        public void CancelAuctionItem(Character player, ulong auctionId)
        {
            var auctionItem = GetAuctionItemFromID(auctionId);

            if (auctionItem.BidderName != "") return;// Someone has already bid on the item and we do not want to remove it. 

            var moneyToSubtract = auctionItem.DirectMoney * .1f;
            var itemList = new Item[10].ToList();
            var newItem = ItemManager.Instance.Create(auctionItem.ItemID, (int)auctionItem.StackSize, auctionItem.Grade);
            itemList[0] = newItem;

            // TODO: Read this from saved data
            var recalculatedFee = (auctionItem.DirectMoney * .01) * (auctionItem.Duration + 1);
            if (recalculatedFee > MaxListingFee) recalculatedFee = MaxListingFee;

            var cancelMail = new MailForAuction(newItem, auctionItem.ClientId, auctionItem.DirectMoney, (int)recalculatedFee);
            cancelMail.FinalizeForCancel();
            cancelMail.Send();
            // MailManager.Instance.SendMail(0, auctionItem.ClientName, "AuctionHouse", "Cancelled Listing", "See attaached.", 1, new int[3], 0, itemList);

            RemoveAuctionItem(auctionItem);
            player.SendPacket(new SCAuctionCanceledPacket(auctionItem));
        }

        private AuctionItem GetAuctionItemFromID(ulong auctionId)
        {
            var item = _auctionItems.Single(c => c.ID == auctionId);

            return item;
        }
        public void BidOnAuctionItem(Character player, ulong auctionId, int bidAmount)
        {
            var auctionItem = GetAuctionItemFromID(auctionId);

            if (auctionItem == null)
            {
                player.SendErrorMessage(Models.Game.Error.ErrorMessageType.AucInvalidEntry);
                return;
            }

            if (auctionItem.BidderId == player.Id)
            {
                player.SendErrorMessage(Models.Game.Error.ErrorMessageType.AucBidSelf);
                return;
            }
            
            if (bidAmount >= auctionItem.DirectMoney && auctionItem.DirectMoney != 0) //Buy now
            {
                if (auctionItem.BidderId != 0) // send mail to person who bid if item was bought at full price. 
                {
                    var newMail = new MailForAuction(auctionItem.ItemID, auctionItem.ClientId, auctionItem.DirectMoney, 0);
                    newMail.FinalizeForBidFail(auctionItem.BidderId, auctionItem.BidMoney);
                    newMail.Send();
                }
                    
                player.SubtractMoney(SlotType.Inventory, (int)auctionItem.DirectMoney);
                RemoveAuctionItemSold(auctionItem, player.Name, auctionItem.DirectMoney);
            }

            else if(bidAmount > auctionItem.BidMoney) //Bid
            {
                if(auctionItem.BidderName != "") //Send mail to old bidder. 
                {
                    var moneyArray = new int[3];
                    moneyArray[0] = (int)auctionItem.BidMoney;

                    // TODO: Read this from saved data
                    var recalculatedFee = (auctionItem.DirectMoney * .01) * (auctionItem.Duration + 1);
                    if (recalculatedFee > MaxListingFee) recalculatedFee = MaxListingFee;

                    var cancelMail = new MailForAuction(auctionItem.ItemID, auctionItem.ClientId, auctionItem.DirectMoney, (int)recalculatedFee);
                    cancelMail.FinalizeForBidFail(auctionItem.BidderId, auctionItem.BidMoney);
                    cancelMail.Send();
                }

                //Set info to new bidders info
                auctionItem.BidderId = player.Id;
                auctionItem.BidderName = player.Name;
                auctionItem.BidMoney = bidAmount;
                auctionItem.GameServerID = 1;

                player.SubtractMoney(SlotType.Inventory, (int)bidAmount);
                player.SendPacket(new SCAuctionBidPacket(auctionItem));
                auctionItem.IsDirty = true;
            } else
            {
                player.SendErrorMessage(Models.Game.Error.ErrorMessageType.AucBidMoneyUnderTopMost);
            }
        }

        public List<AuctionItem> GetAuctionItems(AuctionSearchTemplate searchTemplate)
        {
            List<AuctionItem> auctionItemsFound = new List<AuctionItem>();
            bool myListing = false;

            if (searchTemplate.ItemName == "" && searchTemplate.CategoryA == 0 && searchTemplate.CategoryB == 0 && searchTemplate.CategoryC == 0)
            {
                myListing = true;
                auctionItemsFound = _auctionItems.Where(c => c.ClientId == searchTemplate.PlayerId).ToList();
            }
            
            if(!myListing)
            {
                var itemTemplateList = ItemManager.Instance.GetAllItems();
                var query = from item in itemTemplateList
                    where ((searchTemplate.ItemName != "") ? item.searchString.Contains(searchTemplate.ItemName.ToLower()) : true)
                    where ((searchTemplate.CategoryA != 0) ? searchTemplate.CategoryA == item.AuctionCategoryA : true)
                    where ((searchTemplate.CategoryB != 0) ? searchTemplate.CategoryB == item.AuctionCategoryB : true)
                    where ((searchTemplate.CategoryC != 0) ? searchTemplate.CategoryC == item.AuctionCategoryC : true)
                    select item;

                var selectedItemList = query.ToList();

                auctionItemsFound = _auctionItems.Where(c => selectedItemList.Any(c2 => c2.Id == c.ItemID)).ToList();
            }

            if (searchTemplate.SortKind == 1) //Price
            {
                var sortedList = auctionItemsFound.OrderByDescending(x => x.DirectMoney).ToList();
                auctionItemsFound = sortedList;
                if (searchTemplate.SortOrder == 1)
                    auctionItemsFound.Reverse();
            }

            //TODO 2 Name of item
            //TODO 3 Level of item

            if (searchTemplate.SortKind == 4) //TimeLeft
            {
                var sortedList = auctionItemsFound.OrderByDescending(x => x.TimeLeft).ToList();
                auctionItemsFound = sortedList;
                if (searchTemplate.SortOrder == 1)
                    auctionItemsFound.Reverse();
            }

            if (searchTemplate.Page > 0)
            {
                var startingItemNumber = (int)(searchTemplate.Page * 9);
                var endingitemNumber = (int)((searchTemplate.Page * 9) + 9);
                if (auctionItemsFound.Count > startingItemNumber)
                {
                    var tempItemList = new List<AuctionItem>();
                    for (var i = startingItemNumber; i < endingitemNumber; i++)
                    {
                        if (auctionItemsFound.ElementAtOrDefault(i) != null)
                            tempItemList.Add(auctionItemsFound[i]);
                    }
                    auctionItemsFound = tempItemList;
                }
                else
                    searchTemplate.Page = 0;
            }

            if(auctionItemsFound.Count > 9)
            {
                var tempList = new List<AuctionItem>();

                for (int i = 0; i < 9; i++)
                {
                    tempList.Add(auctionItemsFound[i]);
                }

                auctionItemsFound = tempList;
            }
            return auctionItemsFound;
        }

        public AuctionItem GetCheapestAuctionItem(ulong itemId)
        {
            var tempList = new List<AuctionItem>();

            foreach (var item in _auctionItems)
            {
                if (item.ItemID == itemId)
                    tempList.Add(item);
            }

            if (tempList.Count > 0)
            {
                tempList.OrderByDescending(x => x.DirectMoney);
                return tempList[0];
            }
            else
            {
                return null;
            }
        }

        public string GetLocalizedItemNameById(uint id)
        {
            return LocalizationManager.Instance.Get("items", "name", id, ItemManager.Instance.GetTemplate(id).Name ?? "");
        }

        public ulong GetNextId()
        {
            ulong nextId = 0;
            foreach (var item in _auctionItems)
            {
                if (nextId < item.ID)
                    nextId = item.ID;
            }
            return nextId + 1;
        }

        public void RemoveAuctionItem(AuctionItem itemToRemove)
        {
            if (itemToRemove.ClientName == "") //Testing feature. Relists an item if the server listed it. 
            {
                itemToRemove.EndTime = DateTime.UtcNow.AddSeconds(172800);
                return;
            }
            lock (_auctionItems)
            {
                lock (_deletedAuctionItemIds)
                {
                    if (_auctionItems.Contains(itemToRemove))
                    {
                        _deletedAuctionItemIds.Add((long)itemToRemove.ID);
                        _auctionItems.Remove(itemToRemove);
                    }
                }
            }
        }

        public void AddAuctionItem(AuctionItem itemToAdd)
        {
            lock (_auctionItems)
            {
                _auctionItems.Add(itemToAdd);
            }
        }

        public void UpdateAuctionHouse()
        {
            _log.Trace("Updating Auction House!");
            lock (_auctionItems)
                {
                    var itemsToRemove = _auctionItems.Where(c => DateTime.UtcNow > c.EndTime);

                    foreach (var item in itemsToRemove)
                    {
                        if (item.BidderId != 0)
                            RemoveAuctionItemSold(item, item.BidderName, item.BidMoney);
                        else
                            RemoveAuctionItemFail(item);
                    }
                }
            }

        public AuctionItem CreateAuctionItem(Character player, Item itemToList, int startPrice, int buyoutPrice, byte duration)
        {
            var newItem = itemToList;

            ulong timeLeft;
            switch (duration)
            {
                case 0:
                    timeLeft = 6; //6 hours
                    break;
                case 1:
                    timeLeft = 12; //12 hours
                    break;
                case 2:
                    timeLeft = 24; //24 hours
                    break;
                case 3:
                    timeLeft = 48; //48 hours
                    break;
                default:
                    timeLeft = 6; //default to 6 hours
                    break;
            }
            
            var newAuctionItem = new AuctionItem
            {
                ID = GetNextId(),
                Duration = 5,
                ItemID = newItem.Template.Id,
                ObjectID = 0,
                Grade = newItem.Grade,
                Flags = newItem.ItemFlags,
                StackSize = (uint)newItem.Count,
                DetailType = 0,
                CreationTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddHours(timeLeft),
                LifespanMins = 0,
                Type1 = 0,
                WorldId = 1,
                UnpackDateTIme = DateTime.UtcNow,
                UnsecureDateTime = DateTime.UtcNow,
                WorldId2 = 0,
                ClientId = player.Id,
                ClientName = player.Name,
                StartMoney = startPrice,
                DirectMoney = buyoutPrice,
                GameServerID = 0,
                BidderId = 0,
                BidderName = "",
                BidMoney = 0,
                Extra = 0,
                IsDirty = true
            };
            return newAuctionItem;
        }

        public void Load()
        {
            _auctionItems = new List<AuctionItem>();
            _deletedAuctionItemIds = new List<long>();
            using (var connection = MySQL.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = $"SELECT * FROM auction_house";
                    command.Prepare();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var auctionItem = new AuctionItem();
                            auctionItem.ID = reader.GetUInt32("id");
                            auctionItem.Duration = reader.GetByte("duration"); //0 is 6 hours, 1 is 12 hours, 2 is 24 hours, 3 is 48 hours
                            auctionItem.ItemID = reader.GetUInt32("item_id");
                            auctionItem.ObjectID = reader.GetUInt32("object_id");
                            auctionItem.Grade = reader.GetByte("grade");
                            auctionItem.Flags = (ItemFlag)reader.GetByte("flags");
                            auctionItem.StackSize = reader.GetUInt32("stack_size");
                            auctionItem.DetailType = reader.GetByte("detail_type");
                            auctionItem.CreationTime = reader.GetDateTime("creation_time");
                            auctionItem.EndTime = reader.GetDateTime("end_time");
                            auctionItem.LifespanMins = reader.GetUInt32("lifespan_mins");
                            auctionItem.Type1 = reader.GetUInt32("type_1");
                            auctionItem.WorldId = reader.GetByte("world_id");
                            auctionItem.UnsecureDateTime = reader.GetDateTime("unsecure_date_time");
                            auctionItem.UnpackDateTIme = reader.GetDateTime("unpack_date_time");
                            auctionItem.WorldId2 = reader.GetByte("world_id_2");
                            auctionItem.ClientId = reader.GetUInt32("client_id");
                            auctionItem.ClientName = reader.GetString("client_name");
                            auctionItem.StartMoney = reader.GetInt32("start_money");
                            auctionItem.DirectMoney = reader.GetInt32("direct_money");
                            auctionItem.GameServerID = reader.GetByte("bid_world_id");
                            auctionItem.BidderId = reader.GetUInt32("bidder_id");
                            auctionItem.BidderName = reader.GetString("bidder_name");
                            auctionItem.BidMoney = reader.GetInt32("bid_money");
                            auctionItem.Extra = reader.GetUInt32("extra");
                            AddAuctionItem(auctionItem);
                        }
                    }
                }
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

            var dirtyItems = _auctionItems.Where(c => c.IsDirty == true);
            foreach (var mtbs in dirtyItems)
            {
                using (var command = connection.CreateCommand())
                {
                    command.Connection = connection;
                    command.Transaction = transaction;
                    command.CommandText = "REPLACE INTO auction_house(" +
                        "`id`, `duration`, `item_id`, `object_id`, `grade`, `flags`, `stack_size`, `detail_type`," +
                        " `creation_time`,`end_time`, `lifespan_mins`, `type_1`, `world_id`, `unsecure_date_time`, `unpack_date_time`," +
                        " `world_id_2`, `client_id`, `client_name`, `start_money`, `direct_money`, `bid_world_id`," +
                        " `bidder_id`, `bidder_name`, `bid_money`, `extra`" +
                        ") VALUES (" +
                        "@id, @duration, @item_id, @object_id, @grade, @flags, @stack_size, @detail_type," +
                        " @creation_time, @end_time, @lifespan_mins, @type_1, @world_id, @unsecure_date_time, @unpack_date_time," +
                        " @world_id_2, @client_id, @client_name, @start_money, @direct_money, @bid_world_id," +
                        " @bidder_id, @bidder_name, @bid_money, @extra)";

                    command.Prepare();

                    command.Parameters.AddWithValue("@id", mtbs.ID);
                    command.Parameters.AddWithValue("@duration", mtbs.Duration);
                    command.Parameters.AddWithValue("@item_id", mtbs.ItemID);
                    command.Parameters.AddWithValue("@object_id", mtbs.ObjectID);
                    command.Parameters.AddWithValue("@grade", mtbs.Grade);
                    command.Parameters.AddWithValue("@flags", mtbs.Flags);
                    command.Parameters.AddWithValue("@stack_size", mtbs.StackSize);
                    command.Parameters.AddWithValue("@detail_type", mtbs.DetailType);
                    command.Parameters.AddWithValue("@creation_time", mtbs.CreationTime);
                    command.Parameters.AddWithValue("@end_time",mtbs.EndTime);
                    command.Parameters.AddWithValue("@lifespan_mins", mtbs.LifespanMins);
                    command.Parameters.AddWithValue("@type_1", mtbs.Type1);
                    command.Parameters.AddWithValue("@world_id", mtbs.WorldId);
                    command.Parameters.AddWithValue("@unsecure_date_time", mtbs.UnsecureDateTime);
                    command.Parameters.AddWithValue("@unpack_date_time", mtbs.UnpackDateTIme);
                    command.Parameters.AddWithValue("@world_id_2", mtbs.WorldId2);
                    command.Parameters.AddWithValue("@client_id", mtbs.ClientId);
                    command.Parameters.AddWithValue("@client_name", mtbs.ClientName);
                    command.Parameters.AddWithValue("@start_money", mtbs.StartMoney);
                    command.Parameters.AddWithValue("@direct_money", mtbs.DirectMoney);
                    command.Parameters.AddWithValue("@time_left", mtbs.TimeLeft);
                    command.Parameters.AddWithValue("@bid_world_id", mtbs.GameServerID);
                    command.Parameters.AddWithValue("@bidder_id", mtbs.BidderId);
                    command.Parameters.AddWithValue("@bidder_name", mtbs.BidderName);
                    command.Parameters.AddWithValue("@bid_money", mtbs.BidMoney);
                    command.Parameters.AddWithValue("@extra", mtbs.Extra);

                    command.ExecuteNonQuery();
                    updatedCount++;
                    mtbs.IsDirty = false;
                }

            }

            return (updatedCount, deletedCount);
        }
    }
}
