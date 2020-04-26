using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Auction;
using AAEmu.Game.Models.Game.Auction.Templates;
using AAEmu.Game.Utils.DB;

namespace AAEmu.Game.Core.Managers
{
    class AuctionManager : Singleton<AuctionManager>
    {
        public List<AuctionItem> _auctionItems;
        public Dictionary<uint, string> _en_localizations;

        public void AddAuctionItem()
        {

        }

        public List<AuctionItem> GetAuctionItems(AuctionSearchTemplate searchTemplate)
        {
            List<AuctionItem> auctionItemsFound = new List<AuctionItem>();

            if (searchTemplate.ItemName == "" && searchTemplate.CategoryA == 0 && searchTemplate.Page == 0)
            {
                foreach (var item in _auctionItems)
                {
                    if (item.ClientName == searchTemplate.Player.Name)
                        auctionItemsFound.Add(item);
                }
                return auctionItemsFound;
            }

            var itemTemplates = ItemManager.Instance.GetItemTemplates(searchTemplate);

            foreach (var template in itemTemplates)
            {
                var query = from item in _auctionItems
                            where ((searchTemplate.ItemName != "") ? template.Id == item.ItemID : true)
                            where ((searchTemplate.CategoryA != 0) ? template.AuctionCategoryA == item.Type1 : true)
                            select item;

                var foundItems = query.ToList<AuctionItem>();

                foreach (var item in foundItems)
                {
                    if(!auctionItemsFound.Contains(item))
                        auctionItemsFound.Add(item);
                }
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
                            auctionItem.Asked = reader.GetUInt32("asked");
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
        }
    }
}
