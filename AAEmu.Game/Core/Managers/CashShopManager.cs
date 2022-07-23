using System;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.CashShop;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    class CashShopManager : Singleton<CashShopManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private List<CashShopItem> _cashShopItem;
        private Dictionary<uint, CashShopItemDetail> _cashShopItemDetail;
        private Dictionary<uint, object> _locks = new Dictionary<uint, object>();

        public void CreditDisperseTick(TimeSpan delta)
        {
            var characters = WorldManager.Instance.GetAllCharacters();
            
            foreach(var character in characters)
            {
                AddCredits(character.AccountId, 100);
                character.SendMessage("You have received 100 credits.");
            }
        }

        public int GetAccountCredits(uint accountId)
        {
            object accLock;
            lock(_locks)
            {
                if (!_locks.TryGetValue(accountId, out accLock))
                {
                    accLock = new object();
                    _locks.Add(accountId, accLock);
                }
            }
            lock (accLock)
            {
                try
                {
                    using (var connection = MySQL.CreateConnection())
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = "SELECT credits FROM accounts WHERE account_id = @acc_id";
                            command.Parameters.AddWithValue("@acc_id", accountId);
                            command.Prepare();
                            using (var reader = command.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    return reader.GetInt32("credits");
                                }
                                else
                                {
                                    reader.Close();
                                    command.CommandText = "INSERT INTO accounts (account_id, credits) VALUES (@acc_id, 0)";
                                    command.Prepare();
                                    command.ExecuteNonQuery();
                                    return 0;
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    _log.Error(e.Message);
                    return 0;
                }
            }
        }

        public bool AddCredits(uint accountId, int creditsAmt)
        {
            object accLock;
            lock (_locks)
            {
                if (!_locks.TryGetValue(accountId, out accLock))
                {
                    accLock = new object();
                    _locks.Add(accountId, accLock);
                }
            }
            lock (accLock)
            {
                try
                {
                    using (var connection = MySQL.CreateConnection())
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = 
                                "INSERT INTO accounts (account_id, credits) VALUES(@acc_id, @credits_amt) ON DUPLICATE KEY UPDATE credits = credits + @credits_amt";
                            command.Parameters.AddWithValue("@acc_id", accountId);
                            command.Parameters.AddWithValue("@credits_amt", creditsAmt);
                            command.Prepare();
                            if (command.ExecuteNonQuery() > 0)
                                return true;
                            else
                                return false;
                        }
                    }
                }
                catch (Exception e)
                {
                    _log.Error("{0}\n{1}", e.Message, e.StackTrace);
                    return false;
                }
            }
        }

        public bool RemoveCredits(uint accountId, int credits) => AddCredits(accountId, -credits);

        public void Load()
        {
            _cashShopItem = new List<CashShopItem>();
            _cashShopItemDetail = new Dictionary<uint, CashShopItemDetail>();

            using (var connection = MySQL.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM cash_shop_item";
                    command.Prepare();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var cashShopItem = new CashShopItem();
                            var cashShopItemDetail = new CashShopItemDetail();

                            cashShopItemDetail.CashShopId = cashShopItem.CashShopId = reader.GetUInt32("id");
                            cashShopItemDetail.CashUniqId =  reader.GetUInt32("uniq_id");

                            cashShopItem.CashName = reader.GetString("cash_name");
                            cashShopItem.MainTab = reader.GetByte("main_tab");
                            cashShopItem.SubTab = reader.GetByte("sub_tab");
                            cashShopItem.LevelMin = reader.GetByte("level_min");
                            cashShopItem.LevelMax = reader.GetByte("level_max");

                            cashShopItemDetail.ItemTemplateId = cashShopItem.ItemTemplateId = reader.GetUInt32("item_template_id");

                            cashShopItem.IsSell = reader.GetByte("is_sell");
                            cashShopItem.IsHidden = reader.GetByte("is_hidden");
                            cashShopItem.LimitType = reader.GetByte("limit_type");
                            cashShopItem.BuyCount = reader.GetUInt16("buy_count");
                            cashShopItem.BuyType = reader.GetByte("buy_type");
                            cashShopItem.BuyId = reader.GetUInt32("buy_id");
                            cashShopItem.SDate = reader.GetDateTime("start_date");
                            cashShopItem.EDate = reader.GetDateTime("end_date");

                            cashShopItemDetail.PriceType = cashShopItem.Type = reader.GetByte("type");
                            cashShopItemDetail.Price = cashShopItem.Price = reader.GetUInt32("price");

                            cashShopItem.Remain = reader.GetUInt32("remain");
                            cashShopItem.BonusType = reader.GetInt32("bonus_type");
                            cashShopItem.BonusCount = reader.GetUInt32("bouns_count");
                            cashShopItem.CmdUi = reader.GetByte("cmd_ui");

                            cashShopItemDetail.ItemCount = reader.GetUInt32("item_count");
                            cashShopItemDetail.SelectType = reader.GetByte("select_type");
                            cashShopItemDetail.DefaultFlag = reader.GetByte("default_flag");
                            cashShopItemDetail.EventType = reader.GetByte("event_type");
                            cashShopItemDetail.EventDate = reader.GetDateTime("event_date");
                            cashShopItemDetail.DisPrice = reader.GetUInt32("dis_price");

                            _cashShopItem.Add(cashShopItem);
                            _cashShopItemDetail.Add(cashShopItem.CashShopId, cashShopItemDetail);
                        }

                    }
                }
            }
        }

        public void Initialize()
        {
            TickManager.Instance.OnTick.Subscribe(CreditDisperseTick, TimeSpan.FromMinutes(5));
        }

        public List<CashShopItem> GetCashShopItems()
        {
            return _cashShopItem;
        }

        public CashShopItem GetCashShopItem(uint cashShopId)
        {
            return _cashShopItem.Find(a => a.CashShopId == cashShopId);
        }

        public List<CashShopItem> GetCashShopItems(sbyte mainTab,sbyte subTab,ushort page)
        {
            return _cashShopItem.FindAll(a=>a.MainTab==mainTab && a.SubTab == subTab);
        }

        public CashShopItemDetail GetCashShopItemDetail(uint cashShopId)
        {
            return _cashShopItemDetail.ContainsKey(cashShopId) ? _cashShopItemDetail[cashShopId] : new CashShopItemDetail();
        }
    }
}
