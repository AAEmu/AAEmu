using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
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

    public Dictionary<uint, IcsSku> SKUs { get; set; } = new();
    public Dictionary<uint, IcsItem> ShopItems { get; set; } = new();
    public List<IcsMenu> MenuItems { get; set; } = new();

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

    public void LoadOld()
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

    public void Load()
    {
        SKUs.Clear();
        ShopItems.Clear();
        MenuItems.Clear();

        using var connection = MySQL.CreateConnection();

        // Load SKUs
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM ics_skus ORDER BY shop_id, position";
            command.Prepare();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var entry = new IcsSku();

                entry.Sku = reader.GetUInt32("sku");
                entry.ShopId = reader.GetUInt32("shop_id");
                entry.Position = reader.GetInt32("position");
                entry.ItemId = reader.GetUInt32("item_id");
                entry.ItemCount = reader.GetUInt32("item_count");
                entry.SelectType = reader.GetByte("select-type");
                entry.IsDefault = reader.GetBoolean("is_default");
                entry.EventType = reader.GetByte("event_type");
                entry.EventEndDate = reader.GetDateTime("event_end_date");
                entry.Currency = (CashShopCurrencyType)reader.GetByte("currency");
                entry.Price = reader.GetUInt32("currency");
                entry.DiscountPrice = reader.GetUInt32("discount_price");
                entry.BonusItemId = reader.GetUInt32("bonus_item_id");
                entry.BonusItemCount = reader.GetUInt32("bonus_item_count");

                if (!SKUs.TryAdd(entry.Sku, entry))
                    Logger.Error($"Duplicate SKU {entry.Sku}");
            }
        }

        // Load Shop Items
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM ics_shop_items";
            command.Prepare();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var entry = new IcsItem();

                entry.ShopId = reader.GetUInt32("shop_id");
                entry.DisplayItemId = reader.GetUInt32("display_item_id");
                entry.Name = reader.GetString("name");
                entry.LimitedType = (CashShopLimitType)reader.GetByte("limited_type");
                entry.LimitedStockMax = reader.GetUInt16("limited_stock_max");
                entry.LevelMin = reader.GetByte("level_min");
                entry.LevelMax = reader.GetByte("level_max");
                entry.BuyRestrictType = (CashShopRestrictSaleType)reader.GetByte("buy_restrict_type");
                entry.BuyRestrictId = reader.GetUInt32("buy_restrict_id");
                entry.IsSale = reader.GetBoolean("is_sale");
                entry.IsHidden = reader.GetBoolean("is_hidden");
                entry.SaleStart = reader.GetDateTime("sale_start");
                entry.SaleEnd = reader.GetDateTime("sale_end");
                entry.ShopButtons = (CashShopCmdUiType)reader.GetByte("shop_buttons");

                if (!ShopItems.TryAdd(entry.ShopId, entry))
                    Logger.Error($"Duplicate ShopItem {entry.ShopId}");
            }
        }

        // Attach SKUs to Shop Items
        foreach (var (key, sku) in SKUs)
        {
            if (ShopItems.TryGetValue(sku.ShopId, out var shopItem))
            {
                shopItem.Skus.Add(sku.Sku, sku);
            }
            else
            {
                Logger.Warn($"Found SKU without a valid Shop Item SKU: {key}, ShopItem: {sku.ShopId}");
            }
        }

        // Verify if all Shop Items have at least one SKU attached
        foreach (var (key, shopItem) in ShopItems)
        {
            if (shopItem.Skus.Count < 1)
                Logger.Error($"Shop Item found without any SKUs attached {key}");
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM ics_menu ORDER BY main_tab, sub_tab, tab_position";
            command.Prepare();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var shopItemId = reader.GetUInt32("shop_id");
                if (!ShopItems.TryGetValue(shopItemId, out var shopItem))
                {
                    Logger.Warn($"Menu Entry without a valid ShopId: {shopItemId}");
                    continue;
                }

                var entry = new IcsMenu();
                entry.MainTab = reader.GetByte("main_tab");
                entry.SubTab = reader.GetByte("sub_tab");
                entry.TabPos = reader.GetUInt16("tab_pos");
                entry.ShopItem = shopItem;

                // Note that this List should technically always be in order by main, sub and position
                MenuItems.Add(entry);
            }
        }

        // If something didn't load, force close the shop
        if ((MenuItems.Count <= 0) ||  (ShopItems.Count <= 0) || (SKUs.Count <= 0))
            DisableShop();
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

        for (var i = 0u; i < 20; i++)
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
            cashShopItem.LimitType = CashShopLimitType.None;
            cashShopItem.BuyLimitCount = 5;
            cashShopItem.BuyRestrictType = CashShopRestrictSaleType.None;
            cashShopItem.BuyRestrictId = 0;
            cashShopItem.SDate = DateTime.MinValue;
            cashShopItem.EDate = DateTime.MinValue;

            cashShopItemDetail.CurrencyType = cashShopItem.CurrencyType = CashShopCurrencyType.Credits;
            cashShopItemDetail.Price = cashShopItem.Price = 1100 + i;
            cashShopItemDetail.DisPrice = 2000;

            cashShopItem.Remain = 0;
            cashShopItemDetail.BonusItem = cashShopItem.BonusType = 0;// 28298u + i;
            cashShopItemDetail.BonusCount = cashShopItem.BonusCount = 0; // 5;
            cashShopItem.CmdUi = CashShopCmdUiType.OnlyBuyAllowed;

            cashShopItemDetail.SelectType = 0; // does weird things with the item count ?
            cashShopItemDetail.DefaultFlag = 0; // no idea what this does
            cashShopItemDetail.EventType = 4; // 4 is supposed to be "New", but doesn't work?
            cashShopItemDetail.EventDate = DateTime.MinValue; // DateTime.Today.AddDays(3); // End Sale date
            cashShopItemDetail.DisPrice = 2100; // does this even work?

            cashShopItem.Remain = (cashShopItem.SubTab == 1) ? 250u : 0u;

            CashShopItem.Add(cashShopItem);
            CashShopItemDetail.Add(cashShopItem.CashShopId, cashShopItemDetail);
        }
    }

    public void SendICSPage(GameConnection connection, byte mainTabId, byte subTabId, ushort page)
    {
        var thisTabItems = MenuItems.Where(t => t.MainTab == mainTabId && t.SubTab == subTabId).ToList();
        var isLimitedTab = (mainTabId == 1) && (subTabId == 1);
        var itemsPerPage = isLimitedTab ? 4 : 8;
        var numberOfPages = (ushort)Math.Ceiling((float)thisTabItems.Count() / itemsPerPage);
        var thisPageItems = thisTabItems.Skip(itemsPerPage * (page - 1)).Take(itemsPerPage).ToList();

        for (var i = 0; i < thisPageItems.Count; i++)
        {
            var isLast = i == thisTabItems.Count - 1;
            var shopItem = thisTabItems[i].ShopItem;
            if (shopItem == null)
                continue;

            connection.SendPacket(new SCICSGoodListPacket(isLast, numberOfPages, mainTabId, subTabId, shopItem));
        }

        for (var i = 0; i < thisPageItems.Count; i++)
        {
            var isLast = i == thisTabItems.Count - 1;
            var shopItem = thisTabItems[i].ShopItem;
            if (shopItem == null)
                continue;

            foreach (var sku in shopItem.Skus.Values)
                connection.SendPacket(new SCICSGoodDetailPacket(isLast, sku));
        }

        /*
        var items = CashShopManager.Instance.GetCashShopItems(mainTabId, subTabId, page);
        var featured = (mainTabId == 1) && (subTabId == 1); //Im sure there is another way to check this..
        var maxPerPage = featured ? 4 : 8;
        var numPages = (ushort)Math.Ceiling((float)items.Count / maxPerPage);
        var pageItems = items.Skip(maxPerPage * (page - 1)).Take(maxPerPage).ToList();

        var i = 0;
        foreach (var item in pageItems)
        {
            i++;
            var itemDetail = CashShopManager.Instance.GetCashShopItemDetail(item.CashShopId);
            var end = i >= pageItems.Count;
            Connection.SendPacket(new SCICSGoodListPacket(end, numPages, item));
            Connection.SendPacket(new SCICSGoodDetailPacket(end, itemDetail));
        }
        */

    }
}
