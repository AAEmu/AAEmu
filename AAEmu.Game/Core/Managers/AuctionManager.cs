using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Auction;
using AAEmu.Game.Models.Game.Auction.Templates;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.DB;
using AAEmu.Game.Models.Game.Items;
using NLog;
using AAEmu.Game.Utils;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Managers
{
    public class AuctionManager : Singleton<AuctionManager>
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();

        public List<AuctionItem> _auctionItems;
        public Dictionary<uint, string> _en_localizations;

        public void ListAuctionItem(Character player, ulong itemTeplateId, uint startPrice, uint buyoutPrice, byte duration)
        {
            var newItem = player.Inventory.GetItem((uint)itemTeplateId);
            var newAuctionItem = new AuctionItem();
            newAuctionItem.ID = GetNextID();
            newAuctionItem.Duration = 5;
            newAuctionItem.ItemID = newItem.Template.Id;
            newAuctionItem.ObjectID = 0;
            newAuctionItem.Grade = newItem.Grade;
            newAuctionItem.Flags = newItem.Bounded;
            newAuctionItem.StackSize = (uint)newItem.Count;
            newAuctionItem.DetailType = 0;
            newAuctionItem.CreationTime = DateTime.Now;
            newAuctionItem.LifespanMins = 0;
            newAuctionItem.Type1 = (uint)newItem.Template.AuctionCategoryA;
            newAuctionItem.WorldId = 0;
            newAuctionItem.UnpackDateTIme = DateTime.Now;
            newAuctionItem.UnsecureDateTime = DateTime.Now;
            newAuctionItem.WorldId2 = 0;
            newAuctionItem.Type2 = (uint)newItem.Template.AuctionCategoryB;
            newAuctionItem.ClientName = player.Name;
            newAuctionItem.StartMoney = startPrice;
            newAuctionItem.DirectMoney = buyoutPrice;
            if (duration == 0)
                newAuctionItem.TimeLeft = 21600;
            else
                newAuctionItem.TimeLeft = (ulong)(((duration + 1) * 6) * 3600);
            newAuctionItem.BidWorldID = 0;
            newAuctionItem.Type3 = (uint)newItem.Template.AuctionCategoryC;
            newAuctionItem.BidderName = "";
            newAuctionItem.BidMoney = 0;
            newAuctionItem.Extra = 0;

            var auctionFee = newAuctionItem.DirectMoney * .01;

            if (auctionFee > 1000000)//100 gold max fee
                auctionFee = 1000000;

            player.ChangeMoney(SlotType.Inventory, -(int)auctionFee);
            InventoryHelper.RemoveItemAndUpdateClient(player, newItem, newItem.Count);
            _auctionItems.Add(newAuctionItem);
            player.SendPacket(new SCAuctionPostedPacket(newAuctionItem));
        }

        public void RemoveAuctionItemSold(AuctionItem itemToRemove, string buyer, uint soldAmount)
        {
            if (_auctionItems.Contains(itemToRemove))
            {
                var itemTemplate = ItemManager.Instance.GetItemTemplateFromItemId(itemToRemove.ItemID);
                var newItem = ItemManager.Instance.Create(itemTemplate.Id, (int)itemToRemove.StackSize, itemToRemove.Grade);
                var itemList = new List<Item>();
                itemList.Add(newItem);

                for (int i = 0; i < 9; i++)
                {
                    itemList.Add(null);
                }

                var emptyItemList = new Item[10].ToList();
                var emptyMoneyArray = new int[3];
                var moneyAfterFee = soldAmount * .9;
                var moneyToSend = new int[3];
                moneyToSend[0] = (int)moneyAfterFee;

                MailManager.Instance.SendMail(0, itemToRemove.ClientName, "Auction House", "Succesfull Listing", $"{GetItemNameById(itemToRemove.ItemID)} sold!", 1, moneyToSend, 0, emptyItemList); //Send money to seller
                MailManager.Instance.SendMail(0, buyer, "Auction House", "Succesfull Purchase", "See attached.", 1, emptyMoneyArray, 1, itemList); //Send items to buyer
                _auctionItems.Remove(itemToRemove);
            }
        }

        public void RemoveAuctionItemFail(AuctionItem itemToRemove)
        {
            if(_auctionItems.Contains(itemToRemove))
            {
                var itemTemplate = ItemManager.Instance.GetItemTemplateFromItemId(itemToRemove.ItemID);
                var newItem = ItemManager.Instance.Create(itemTemplate.Id, (int)itemToRemove.StackSize, itemToRemove.Grade);
                var itemList = new List<Item>();
                itemList.Add(newItem);

                for (int i = 0; i < 9; i++)
                {
                    itemList.Add(null);
                }
                var moneyArray = new int[3];

                if (itemToRemove.BidderName == "")//FailedListing
                {

                    MailManager.Instance.SendMail(0, itemToRemove.ClientName, "Auction House", "Failed Listing", "See attached.", 1, moneyArray, 0, itemList);
                    _auctionItems.Remove(itemToRemove);
                }
                else // Player won the bid
                {
                    RemoveAuctionItemSold(itemToRemove, itemToRemove.BidderName, itemToRemove.BidMoney);
                }
            }
        }

        public AuctionItem GetAuctionItemFromID(ulong auctionId)
        {
            foreach (var item in _auctionItems)
            {
                if(item.ID == auctionId)
                {
                    return item;
                }
            }
            return null;
        }

        public void BidOnAuctionItem(Character player, ulong auctionId, string biddersName, uint bidAmount)
        {
            var auctionItem = GetAuctionItemFromID(auctionId);
            if(auctionItem != null)
            {
                if (bidAmount >= auctionItem.BidMoney) //Buy now
                    RemoveAuctionItemSold(auctionItem, biddersName, auctionItem.DirectMoney);

                else if(bidAmount > auctionItem.BidMoney)
                {
                    if(auctionItem.BidderName != biddersName)
                    {

                        auctionItem.BidderName = biddersName;
                        auctionItem.BidMoney = bidAmount;
                        var biddingPlayer = WorldManager.Instance.GetCharacter(biddersName);
                        biddingPlayer.ChangeMoney(SlotType.Inventory, -(int)bidAmount);
                        biddingPlayer.SendPacket(new SCAuctionBidPacket(auctionItem));
                        //TODO send mail back to player who is losing bid
                    }
                }
            }
        }

        public List<AuctionItem> GetAuctionItems(AuctionSearchTemplate searchTemplate)
        {
            List<AuctionItem> auctionItemsFound = new List<AuctionItem>();
            bool myListing = false;

            if (searchTemplate.Type == 1)
            {
                myListing = true;
                if (searchTemplate.Page > 0)
                {
                    var startingItemNumber = (int)(searchTemplate.Page * 9);
                    var endingitemNumber = (int)((searchTemplate.Page * 9) + 8);
                    if (auctionItemsFound.Count > startingItemNumber)
                    {
                        var tempItemList = new List<AuctionItem>();
                        for (int i = startingItemNumber; i < endingitemNumber; i++)
                        {
                            if (auctionItemsFound.ElementAtOrDefault(i) != null && auctionItemsFound[i].ClientName == searchTemplate.Player.Name)
                                tempItemList.Add(auctionItemsFound[i]);
                        }
                        auctionItemsFound = tempItemList;
                    }
                    else
                        searchTemplate.Page = 0;
                }
                else
                {
                    foreach (var item in _auctionItems)
                    {
                        if (item.ClientName == searchTemplate.Player.Name)
                            auctionItemsFound.Add(item);
                    }
                }
            }

            if(!myListing)
            {
                var itemTemplates = ItemManager.Instance.GetItemTemplates(searchTemplate);

                foreach (var template in itemTemplates)
                {
                    var query = from item in _auctionItems
                                where ((searchTemplate.ItemName != "") ? template.Id == item.ItemID : true)
                                where ((searchTemplate.CategoryA != 0) ? template.AuctionCategoryA == item.Type1 : true)
                                where ((searchTemplate.CategoryB != 0) ? template.AuctionCategoryB == item.Type2 : true)
                                where ((searchTemplate.CategoryC != 0) ? template.AuctionCategoryC == item.Type3 : true)
                                select item;

                    var foundItems = query.ToList<AuctionItem>();

                    foreach (var item in foundItems)
                    {
                        if (!auctionItemsFound.Contains(item))
                            auctionItemsFound.Add(item);
                    }
                }
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
                var endingitemNumber = (int)((searchTemplate.Page * 9) + 8);
                if (auctionItemsFound.Count > startingItemNumber)
                {
                    var tempItemList = new List<AuctionItem>();
                    for (int i = startingItemNumber; i < endingitemNumber; i++)
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

        public List<uint> GetItemIdsFromName(string itemName)
        {
            var query = from item in _en_localizations
                        where (item.Value.Contains(itemName.ToLower()))
                        select item.Key;

            var itemIdList = query.ToList();

            return itemIdList;
        }

        public string GetItemNameById(uint id)
        {
            foreach (var item in _en_localizations)
            {
                if (item.Key == id)
                    return item.Value;
            }
            return "";
        }

        public void UpdateAuctionHouse()
        {
            _log.Info("Updating Auction House!");
            foreach (var item in _auctionItems.ToList())
            {
                var timeLeft = (ulong)(DateTime.Now - item.CreationTime).TotalSeconds;
                if (timeLeft > item.TimeLeft)
                    RemoveAuctionItemFail(item);
                else
                {
                    item.TimeLeft -= 5;
                }
            }
        }

        public ulong GetNextID()
        {
            ulong nextId = 0;
            foreach (var item in _auctionItems)
            {
                if (nextId < item.ID)
                    nextId = item.ID;
            }
            return nextId + 1;
        }

        public int[] ConvertMoneyToArray(int m)
        {
            int[] result = new int[3];
            int copper = m % 100;
            m = (m - copper) / 100;
            int silver = m % 100;
            int gold = (m - silver) / 100;
            result[2] = copper;
            result[1] = silver;
            result[0] = gold;
            return result;
        }

        public void Load()
        {
            _auctionItems = new List<AuctionItem>();
            _en_localizations = new Dictionary<uint, string>();
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
                            auctionItem.Duration = reader.GetByte("duration"); //0 is 6 hours, 1 is 12 hours, 2 is 18 hours, 3 is 24 hours
                            auctionItem.ItemID = reader.GetUInt32("item_id");
                            auctionItem.ObjectID = reader.GetUInt32("object_id");
                            auctionItem.Grade = reader.GetByte("grade");
                            auctionItem.Flags = reader.GetByte("flags");
                            auctionItem.StackSize = reader.GetUInt32("stack_size");
                            auctionItem.DetailType = reader.GetByte("detail_type");
                            auctionItem.CreationTime = reader.GetDateTime("creation_time");
                            auctionItem.LifespanMins = reader.GetUInt32("lifespan_mins");
                            auctionItem.Type1 = reader.GetUInt32("type_1");
                            auctionItem.WorldId = reader.GetByte("world_id");
                            auctionItem.UnsecureDateTime = reader.GetDateTime("unsecure_date_time");
                            auctionItem.UnpackDateTIme = reader.GetDateTime("unpack_date_time");
                            auctionItem.WorldId2 = reader.GetByte("world_id_2");
                            auctionItem.Type2 = reader.GetUInt32("type_2");
                            auctionItem.ClientName = reader.GetString("client_name");
                            auctionItem.StartMoney = reader.GetUInt32("start_money");
                            auctionItem.DirectMoney = reader.GetUInt32("direct_money");
                            auctionItem.TimeLeft = reader.GetUInt32("time_left");
                            auctionItem.BidWorldID = reader.GetByte("bid_world_id");
                            auctionItem.Type3 = reader.GetUInt32("type_3");
                            auctionItem.BidderName = reader.GetString("bidder_name");
                            auctionItem.BidMoney = reader.GetUInt32("bid_money");
                            auctionItem.Extra = reader.GetUInt32("extra");
                            _auctionItems.Add(auctionItem);
                        }
                    }
                }
            }
            using (var connection = SQLite.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = $"SELECT * FROM localized_texts WHERE tbl_name='items' AND tbl_column_name='name'";
                    command.Prepare();
                    using (var sqlitereader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqlitereader))
                    {
                        while (reader.Read())
                        {
                            var _itemName = reader.GetString("en_us").ToLower();
                            var _itemId = reader.GetUInt32("idx");

                            if(_itemName != "" && _itemId != 0)
                                _en_localizations.Add(_itemId, _itemName);
                        }
                    }
                }
            }
            var auctionTask = new AuctionHouseTask();
            TaskManager.Instance.Schedule(auctionTask, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }
    }
}
