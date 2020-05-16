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
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2L;

namespace AAEmu.Game.Core.Managers
{
    public class AuctionManager : Singleton<AuctionManager>
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();

        public List<AuctionItem> _auctionItems;
        public Dictionary<uint, string> _en_localizations;

        public void ListAuctionItem(Character player, ulong itemTeplateId, uint startPrice, uint buyoutPrice, byte duration)
        {
            var newItem = player.Inventory.GetItem((ulong)itemTeplateId);
            var newAuctionItem = CreateAuctionItem(player, newItem, startPrice, buyoutPrice, duration);

            if (newAuctionItem == null) //TODO
                return;

            if (newItem == null) //TODO
                return;

            var auctionFee = (newAuctionItem.DirectMoney * .01) * (duration + 1);

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

                MailManager.Instance.SendMail(0, itemToRemove.ClientName, "Auction House", "Succesfull Listing", $"{GetLocalizedItemNameById(itemToRemove.ItemID)} sold!", 1, moneyToSend, 0, emptyItemList); //Send money to seller
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

        public void CancelAuctionItem(Character player, ulong auctionId)
        {
            var auctionItem = GetAuctionItemFromID(auctionId);

            if(auctionItem != null)
            {
                player.SendPacket(new SCAuctionCanceledPacket(auctionItem));
                var moneyToSubtract = auctionItem.DirectMoney * .01f;
                player.ChangeMoney(SlotType.None, -(int)moneyToSubtract); //TODO not correct way to subtract money. 
                //TODO Mail item to player.
                _auctionItems.Remove(auctionItem);
            }
        }

        public AuctionItem GetAuctionItemFromID(ulong auctionId)
        {
            for (int i = 0; i < _auctionItems.Count; i++)
            {
                if (_auctionItems[i].ID == auctionId)
                    return _auctionItems[i];
            }
            return null;
        }

        public void BidOnAuctionItem(Character player, ulong auctionId, uint bidAmount)
        {
            var auctionItem = GetAuctionItemFromID(auctionId);
            if(auctionItem != null)
            {
                if (bidAmount >= auctionItem.BidMoney) //Buy now
                    RemoveAuctionItemSold(auctionItem, player.Name, auctionItem.DirectMoney);

                else if(bidAmount > auctionItem.BidMoney)
                {
                    if(auctionItem.BidderName != player.Name)
                    {
                        auctionItem.BidderId = player.Id;
                        auctionItem.BidderName = player.Name;
                        auctionItem.BidMoney = bidAmount;
                        player.ChangeMoney(SlotType.Inventory, -(int)bidAmount);//TODO
                        player.SendPacket(new SCAuctionBidPacket(auctionItem));
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
                            if (auctionItemsFound.ElementAtOrDefault(i) != null && auctionItemsFound[i].ClientId == searchTemplate.Player.Id)
                                tempItemList.Add(auctionItemsFound[i]);
                        }
                        auctionItemsFound = tempItemList;
                    }
                    else
                        searchTemplate.Page = 0;
                }
                else
                {
                    for (int i = 0; i < _auctionItems.Count; i++)
                    {
                        if (_auctionItems[i].ClientName == searchTemplate.Player.Name)
                            auctionItemsFound.Add(_auctionItems[i]);
                    }
                }
            }

            if(!myListing)
            {
                var query = from item in _auctionItems
                                where ((searchTemplate.ItemName != "") ?  item.ItemName.Contains(searchTemplate.ItemName.ToLower()) : true)
                                where ((searchTemplate.CategoryA != 0) ? searchTemplate.CategoryA == item.CategoryA : true)
                                where ((searchTemplate.CategoryB != 0) ? searchTemplate.CategoryB == item.CategoryB : true)
                                where ((searchTemplate.CategoryC != 0) ? searchTemplate.CategoryC == item.CategoryC : true)
                                select item;

                auctionItemsFound = query.ToList<AuctionItem>();
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

        public string GetLocalizedItemNameById(uint id)
        {
            return LocalizationManager.Instance.Get("items", "name", id, ItemManager.Instance.GetTemplate(id).Name ?? "");
        }

        public void UpdateAuctionHouse()
        {
            _log.Debug("Updating Auction House!");

            for (int i = 0; i < _auctionItems.Count; i++)
            {
                var timeLeft = (ulong)(DateTime.Now - _auctionItems[i].CreationTime).TotalSeconds;
                if (timeLeft > _auctionItems[i].TimeLeft)
                    RemoveAuctionItemFail(_auctionItems[i]);
                else
                {
                    _auctionItems[i].TimeLeft -= 5;
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

        public AuctionItem CreateAuctionItem(Character player, Item itemToList, uint startPrice, uint buyoutPrice, byte duration)
        {
            var newItem = itemToList;

            if (newItem == null) //TODO
                return null;

            ulong timeLeft;
            switch (duration)
            {
                case 0:
                    timeLeft = 21600; //6 hourse
                    break;
                case 1:
                    timeLeft = 43200; //12 hours
                    break;
                case 2:
                    timeLeft = 86400; //24 hours
                    break;
                case 3:
                    timeLeft = 172800; //48 hours
                    break;
                default:
                    timeLeft = 21600; //default to 6 hours
                    break;
            }

            var newAuctionItem = new AuctionItem
            {
                ID = GetNextID(),
                Duration = 5,
                ItemID = newItem.Template.Id,
                ItemName = GetLocalizedItemNameById(newItem.Template.Id),
                ObjectID = 0,
                Grade = newItem.Grade,
                Flags = newItem.Flags,
                StackSize = (uint)newItem.Count,
                DetailType = 0,
                CreationTime = DateTime.Now,
                LifespanMins = 0,
                Type1 = 0,
                WorldId = 0,
                UnpackDateTIme = DateTime.Now,
                UnsecureDateTime = DateTime.Now,
                WorldId2 = 0,
                ClientId = player.Id,
                ClientName = player.Name,
                StartMoney = startPrice,
                DirectMoney = buyoutPrice,
                TimeLeft = timeLeft,
                BidWorldID = 0,
                BidderId = 0,
                BidderName = "",
                BidMoney = 0,
                Extra = 0,
                CategoryA = (uint)newItem.Template.AuctionCategoryA,
                CategoryB = (uint)newItem.Template.AuctionCategoryB,
                CategoryC = (uint)newItem.Template.AuctionCategoryC
            };
            return newAuctionItem;
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
                            auctionItem.ItemName = reader.GetString("item_name").ToLower();
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
                            auctionItem.ClientId = reader.GetUInt32("client_id");
                            auctionItem.ClientName = reader.GetString("client_name");
                            auctionItem.StartMoney = reader.GetUInt32("start_money");
                            auctionItem.DirectMoney = reader.GetUInt32("direct_money");
                            auctionItem.TimeLeft = reader.GetUInt32("time_left");
                            auctionItem.BidWorldID = reader.GetByte("bid_world_id");
                            auctionItem.BidderId = reader.GetUInt32("bidder_id");
                            auctionItem.BidderName = reader.GetString("bidder_name");
                            auctionItem.BidMoney = reader.GetUInt32("bid_money");
                            auctionItem.Extra = reader.GetUInt32("extra");
                            auctionItem.CategoryA = reader.GetUInt32("category_a");
                            auctionItem.CategoryB = reader.GetUInt32("category_b");
                            auctionItem.CategoryC = reader.GetUInt32("category_c");
                            _auctionItems.Add(auctionItem);
                        }
                    }
                }
            }
            var auctionTask = new AuctionHouseTask();
            TaskManager.Instance.Schedule(auctionTask, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }
    }
}
