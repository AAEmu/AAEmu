using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
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

    public bool Enabled { get; private set; }

    public Dictionary<uint, IcsSku> SKUs { get; set; } = [];
    public Dictionary<uint, IcsItem> ShopItems { get; set; } = [];
    public List<IcsMenu> MenuItems { get; set; } = [];

    public void CreditDisperseTick(TimeSpan delta)
    {
        var characters = WorldManager.Instance.GetAllCharacters();

        foreach (var character in characters)
        {
            AccountManager.Instance.AddCredits(character.AccountId, 100);
            character.SendMessage("You have received 100 credits.");
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
                entry.SelectType = reader.GetByte("select_type");
                entry.IsDefault = reader.GetBoolean("is_default");
                entry.EventType = reader.GetByte("event_type");
                entry.EventEndDate = reader.IsDBNull(reader.GetOrdinal("event_end_date")) ? DateTime.MinValue : reader.GetDateTime("event_end_date");
                entry.Currency = (CashShopCurrencyType)reader.GetByte("currency");
                entry.Price = reader.GetUInt32("price");
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
                entry.Name = reader.IsDBNull(reader.GetOrdinal("name")) ? "" : reader.GetString("name");
                entry.LimitedType = (CashShopLimitType)reader.GetByte("limited_type");
                entry.LimitedStockMax = reader.GetUInt16("limited_stock_max");
                entry.LevelMin = reader.GetByte("level_min");
                entry.LevelMax = reader.GetByte("level_max");
                entry.BuyRestrictType = (CashShopRestrictSaleType)reader.GetByte("buy_restrict_type");
                entry.BuyRestrictId = reader.GetUInt32("buy_restrict_id");
                entry.IsSale = reader.GetBoolean("is_sale");
                entry.IsHidden = reader.GetBoolean("is_hidden");
                entry.SaleStart = reader.IsDBNull(reader.GetOrdinal("sale_start")) ? DateTime.MinValue : reader.GetDateTime("sale_start");
                entry.SaleEnd = reader.IsDBNull(reader.GetOrdinal("sale_end")) ? DateTime.MinValue : reader.GetDateTime("sale_end");
                entry.Remaining = reader.GetInt32("remaining");
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
                if ((shopItem.Skus.Count <= 0) && string.IsNullOrWhiteSpace(shopItem.Name))
                {
                    // First Item, grab it's name when needed
                    shopItem.Name = LocalizationManager.Instance.Get("items", "name", sku.ItemId) ?? "???";
                }
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
            command.CommandText = "SELECT * FROM ics_menu ORDER BY main_tab, sub_tab, tab_pos";
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
                entry.Id = reader.GetInt64("id");
                entry.MainTab = reader.GetByte("main_tab");
                entry.SubTab = reader.GetByte("sub_tab");
                entry.TabPos = reader.GetUInt16("tab_pos");
                entry.ShopItem = shopItem;

                // Note that this List should technically always be in order by main, sub and position
                MenuItems.Add(entry);
            }
        }

        // If something didn't load, force close the shop
        if ((MenuItems.Count <= 0) || (ShopItems.Count <= 0) || (SKUs.Count <= 0))
            DisableShop();
    }

    public void Initialize()
    {
        // TickManager.Instance.OnTick.Subscribe(CreditDisperseTick, TimeSpan.FromMinutes(5));
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

    public void SendICSPage(GameConnection connection, byte mainTabId, byte subTabId, ushort page)
    {
        var thisTabItems = MenuItems.Where(t => t.MainTab == mainTabId && t.SubTab == subTabId).ToList();
        var isLimitedTab = (mainTabId == 1) && (subTabId == 1);
        var itemsPerPage = isLimitedTab ? 4 : 8;
        var numberOfPages = (ushort)Math.Ceiling((float)thisTabItems.Count / itemsPerPage);
        var thisPageItems = thisTabItems.Skip(itemsPerPage * (page - 1)).Take(itemsPerPage).ToList();

        for (var i = 0; i < thisPageItems.Count; i++)
        {
            var isLast = i == thisPageItems.Count - 1;
            var shopItem = thisPageItems[i].ShopItem;
            if (shopItem == null)
                continue;

            connection.SendPacket(new SCICSGoodListPacket(isLast, numberOfPages, mainTabId, subTabId, shopItem));
        }

        for (var i = 0; i < thisPageItems.Count; i++)
        {
            var isLastItem = i >= thisPageItems.Count - 1;
            var shopItem = thisPageItems[i].ShopItem;
            if (shopItem == null)
                continue;

            var n = 0;
            foreach (var sku in shopItem.Skus.Values)
            {
                var isLastSku = n >= shopItem.Skus.Count - 1;
                connection.SendPacket(new SCICSGoodDetailPacket(isLastSku && isLastItem, sku));
                n++;
            }
        }
    }

    /// <summary>
    /// Returns a list of sales for a specific ShopItem made by accountId or characterId
    /// </summary>
    /// <param name="accountId"></param>
    /// <param name="characterId"></param>
    /// <param name="shopItemId"></param>
    /// <returns>Resulting list of sales</returns>
    public List<AuditIcsSale> GetSalesForShopItem(uint accountId, uint characterId, uint shopItemId)
    {
        var res = new List<AuditIcsSale>();

        if (((accountId == 0) && (characterId == 0)) || (shopItemId <= 0))
            return res;

        using var connection = MySQL.CreateConnection();

        // Load Sales
        using (var command = connection.CreateCommand())
        {
            if (characterId > 0)
            {
                command.CommandText = "SELECT * FROM audit_ics_sales WHERE (buyer_char = @char_id) AND (shop_item_id = @shop_id)";
                command.Parameters.AddWithValue("@char_id", characterId);
            }
            else
            {
                command.CommandText = "SELECT * FROM audit_ics_sales WHERE (buyer_account = @acc_id) AND (shop_item_id = @shop_id)";
                command.Parameters.AddWithValue("@acc_id", accountId);
            }
            command.Parameters.AddWithValue("@shop_id", shopItemId);
            command.Prepare();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var entry = new AuditIcsSale();

                entry.BuyerAccount = reader.GetUInt32("buyer_account");
                entry.BuyerChar = reader.GetUInt32("buyer_char");
                entry.TargetAccount = reader.GetUInt32("target_account");
                entry.TargetChar = reader.GetUInt32("target_char");
                entry.SaleDate = reader.IsDBNull(reader.GetOrdinal("sale_date")) ? DateTime.MinValue : reader.GetDateTime("sale_date");
                entry.ShopItemId = reader.GetUInt32("shop_item_id");
                entry.Sku = reader.GetUInt32("sku"); // The SKU Id can be used to get the exact amount of items sold
                entry.SaleCost = reader.GetInt32("sale_cost");
                entry.SaleCurrency = (CashShopCurrencyType)(reader.GetByte("sale_currency"));
                entry.Description = reader.GetString("description");

                res.Add(entry);
            }
        }
        return res;
    }

    public bool LogSale(uint buyerAccount, uint buyerChar,
        uint targetAccount, uint targetChar,
        DateTime saleDate,
        uint shopItemId, uint sku,
        uint saleCost, CashShopCurrencyType saleCurrency,
        string description)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO audit_ics_sales (buyer_account, buyer_char, target_account, target_char, sale_date, shop_item_id, sku, sale_cost, sale_currency, description) " +
                "VALUES (@buyer_account, @buyer_char, @target_account, @target_char, @sale_date, @shop_item_id, @sku, @sale_cost, @sale_currency, @description)";
            command.Parameters.AddWithValue("@buyer_account", buyerAccount);
            command.Parameters.AddWithValue("@buyer_char", buyerChar);
            command.Parameters.AddWithValue("@target_account", targetAccount);
            command.Parameters.AddWithValue("@target_char", targetChar);
            command.Parameters.AddWithValue("@sale_date", saleDate);
            command.Parameters.AddWithValue("@shop_item_id", shopItemId);
            command.Parameters.AddWithValue("@sku", sku);
            command.Parameters.AddWithValue("@sale_cost", saleCost);
            command.Parameters.AddWithValue("@sale_currency", (byte)saleCurrency);
            command.Parameters.AddWithValue("@description", description);
            command.Prepare();
            if (command.ExecuteNonQuery() <= 0)
            {
                Logger.Error($"Saving sale failed");
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Fatal($"Saving sale failed Exception: {ex}");
            return false;
        }
        return true;
    }

    public bool UpdateRemainingShopItemStock(uint shopItemId, int newRemaining)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE ics_shop_items SET `remaining` = @remaining WHERE `shop_id` = @shop_item";
            command.Parameters.AddWithValue("@remaining", newRemaining);
            command.Parameters.AddWithValue("@shop_item", shopItemId);
            command.Prepare();
            if (command.ExecuteNonQuery() <= 0)
            {
                Logger.Error($"Updating stock failed! ShopItem: {shopItemId} -> {newRemaining}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Fatal($"Stock updating failed Exception: {ex}");
            return false;
        }
        return true;
    }
}
