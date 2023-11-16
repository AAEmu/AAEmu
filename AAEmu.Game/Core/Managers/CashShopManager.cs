using System;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.CashShop;
using AAEmu.Game.Models.StaticValues;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class CashShopManager : Singleton<CashShopManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private List<CashShopItem> CashShopItem { get; set; }
    private Dictionary<uint, CashShopItemDetail> CashShopItemDetail { get; set; }
    private readonly Dictionary<uint, object> _locks = new();
    public bool Enabled { get; private set; }

    public void CreditDisperseTick(TimeSpan delta)
    {
        var characters = WorldManager.Instance.GetAllCharacters();

        foreach (var character in characters)
        {
            AddCredits(character.AccountId, 100);
            character.SendMessage("You have received 100 credits.");
        }
    }

    public int GetAccountCredits(uint accountId)
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
                Logger.Error(e.Message);
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
                using var connection = MySQL.CreateConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "INSERT INTO accounts (account_id, credits) VALUES(@acc_id, @credits_amt) ON DUPLICATE KEY UPDATE credits = credits + @credits_amt";
                command.Parameters.AddWithValue("@acc_id", accountId);
                command.Parameters.AddWithValue("@credits_amt", creditsAmt);
                command.Prepare();
                return command.ExecuteNonQuery() > 0;
            }
            catch (Exception e)
            {
                Logger.Error("{0}\n{1}", e.Message, e.StackTrace);
                return false;
            }
        }
    }

    public bool RemoveCredits(uint accountId, int credits) => AddCredits(accountId, -credits);

    public void Load()
    {
        CashShopItem = new List<CashShopItem>();
        CashShopItemDetail = new Dictionary<uint, CashShopItemDetail>();

        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM cash_shop_item";
        command.Prepare();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var cashShopItem = new CashShopItem();
            var cashShopItemDetail = new CashShopItemDetail();

            cashShopItemDetail.CashShopId = cashShopItem.CashShopId = reader.GetUInt32("id");
            cashShopItemDetail.CashUniqId = reader.GetUInt32("uniq_id");

            cashShopItem.CashName = reader.GetString("cash_name");
            cashShopItem.MainTab = reader.GetByte("main_tab");
            cashShopItem.SubTab = reader.GetByte("sub_tab");
            cashShopItem.LevelMin = reader.GetByte("level_min");
            cashShopItem.LevelMax = reader.GetByte("level_max");

            cashShopItemDetail.ItemTemplateId = cashShopItem.ItemTemplateId = reader.GetUInt32("item_template_id");

            cashShopItem.IsSell = reader.GetByte("is_sell");
            cashShopItem.IsHidden = reader.GetByte("is_hidden");
            cashShopItem.LimitType = (CashShopLimitType)reader.GetByte("limit_type");
            cashShopItem.BuyLimitCount = reader.GetUInt16("buy_count");
            cashShopItem.BuyRestrictType = (CashShopRestrictSaleType)reader.GetByte("buy_type");
            cashShopItem.BuyRestrictId = reader.GetUInt32("buy_id");
            cashShopItem.SDate = reader.GetDateTime("start_date");
            cashShopItem.EDate = reader.GetDateTime("end_date");

            cashShopItemDetail.CurrencyType = cashShopItem.CurrencyType = (CashShopCurrencyType)reader.GetByte("type");
            cashShopItemDetail.Price = cashShopItem.Price = reader.GetUInt32("price");

            cashShopItem.Remain = reader.GetUInt32("remain");
            cashShopItem.BonusType = reader.GetUInt32("bonus_type");
            cashShopItem.BonusCount = reader.GetUInt32("bouns_count"); // Yes, that is a typo, please leave it
            cashShopItem.CmdUi = (CashShopCmdUiType)reader.GetByte("cmd_ui");

            cashShopItemDetail.ItemCount = reader.GetUInt32("item_count");
            cashShopItemDetail.SelectType = reader.GetByte("select_type");
            cashShopItemDetail.DefaultFlag = reader.GetByte("default_flag");
            cashShopItemDetail.EventType = reader.GetByte("event_type");
            cashShopItemDetail.EventDate = reader.GetDateTime("event_date");
            cashShopItemDetail.DisPrice = reader.GetUInt32("dis_price");

            CashShopItem.Add(cashShopItem);
            CashShopItemDetail.Add(cashShopItem.CashShopId, cashShopItemDetail);
        }
    }

    public void Initialize()
    {
        TickManager.Instance.OnTick.Subscribe(CreditDisperseTick, TimeSpan.FromMinutes(5));
    }

    public List<CashShopItem> GetCashShopItems()
    {
        return CashShopItem;
    }

    public CashShopItem GetCashShopItem(uint cashShopId)
    {
        return CashShopItem.Find(a => a.CashShopId == cashShopId);
    }

    public List<CashShopItem> GetCashShopItems(sbyte mainTab, sbyte subTab, ushort page)
    {
        return CashShopItem.FindAll(a => a.MainTab == mainTab && a.SubTab == subTab);
    }

    public CashShopItemDetail GetCashShopItemDetail(uint cashShopId)
    {
        return CashShopItemDetail.TryGetValue(cashShopId, out var value) ? value : new CashShopItemDetail();
    }

    public void EnabledShop()
    {
        Enabled = true;
    }

    public void DisableShop()
    {
        Enabled = false;
        foreach (var character in WorldManager.Instance.GetAllCharacters())
            character?.SendPacket(new SCICSCheckTimePacket());
    }

    public void DebugShopLoad()
    {
        CashShopItem = new List<CashShopItem>();
        CashShopItemDetail = new Dictionary<uint, CashShopItemDetail>();

        for (var i = 0u; i < 16; i++)
        {
            var cashShopItem = new CashShopItem();
            var cashShopItemDetail = new CashShopItemDetail();

            var testItem = 28297u;

            cashShopItemDetail.CashShopId = cashShopItem.CashShopId = 1000 + i;
            cashShopItemDetail.CashUniqId = 1000 + i;

            cashShopItem.CashName = LocalizationManager.Instance.Get("items", "name", testItem, "Unnamed");
            cashShopItem.MainTab = 1;
            cashShopItem.SubTab = (byte)(1 + i / 6);
            cashShopItem.LevelMin = 0;
            cashShopItem.LevelMax = 0;

            cashShopItemDetail.ItemTemplateId = cashShopItem.ItemTemplateId = testItem;

            cashShopItem.IsSell = 0;
            cashShopItem.IsHidden = 0;
            cashShopItemDetail.ItemCount = 1 + (i % 10);
            cashShopItem.LimitType = (CashShopLimitType)(i % 3);
            cashShopItem.BuyLimitCount = 5;
            cashShopItem.BuyRestrictType = CashShopRestrictSaleType.None;
            cashShopItem.BuyRestrictId = 0;
            cashShopItem.SDate = DateTime.MinValue;
            cashShopItem.EDate = DateTime.MinValue;

            cashShopItemDetail.CurrencyType = cashShopItem.CurrencyType = CashShopCurrencyType.Credits;
            cashShopItemDetail.Price = cashShopItem.Price = 1100 + i;
            cashShopItemDetail.DisPrice = 2000;

            cashShopItem.Remain = 0;
            cashShopItemDetail.BonusType = cashShopItem.BonusType = 0;
            cashShopItemDetail.BonusCount = cashShopItem.BonusCount = 0;
            cashShopItem.CmdUi = (CashShopCmdUiType)(i % 4);

            cashShopItemDetail.SelectType = 0;
            cashShopItemDetail.DefaultFlag = 0;
            cashShopItemDetail.EventType = 0;
            cashShopItemDetail.EventDate = DateTime.MinValue;
            cashShopItemDetail.DisPrice = 0;

            cashShopItem.Remain = (cashShopItem.SubTab == 1) ? 250u : 0u;

            CashShopItem.Add(cashShopItem);
            CashShopItemDetail.Add(cashShopItem.CashShopId, cashShopItemDetail);
        }
    }
}
