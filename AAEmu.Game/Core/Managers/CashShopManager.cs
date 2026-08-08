using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.CashShop;
using AAEmu.Game.Models.StaticValues;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class CashShopManager(IWorldManager worldManager, IAccountManager accountManager, ILocalizationManager localizationManager) : Singleton<CashShopManager>, ICashShopManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public bool Enabled { get; private set; }

    public Dictionary<uint, IcsSku> SKUs { get; set; } = [];
    public Dictionary<uint, IcsItem> ShopItems { get; set; } = [];
    public List<IcsMenu> MenuItems { get; set; } = [];

    public void CreditDisperseTick(TimeSpan delta)
    {
        var characters = worldManager.GetAllCharacters();

        foreach (var character in characters)
        {
            accountManager.AddCredits(character.AccountId, 100);
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
                var entry = new IcsSku
                {
                    Sku = reader.GetUInt32("sku"), ShopId = reader.GetUInt32("shop_id"), Position = reader.GetInt32("position"),
                    ItemId = reader.GetUInt32("item_id"),
                    ItemCount = reader.GetUInt32("item_count"),
                    SelectType = reader.GetByte("select_type"),
                    IsDefault = reader.GetBoolean("is_default"),
                    EventType = reader.GetByte("event_type"),
                    EventEndDate = reader.IsDBNull(reader.GetOrdinal("event_end_date")) ? DateTime.MinValue : reader.GetDateTime("event_end_date"),
                    Currency = (CashShopCurrencyType)reader.GetByte("currency"),
                    Price = reader.GetUInt32("price"),
                    DiscountPrice = reader.GetUInt32("discount_price"),
                    BonusItemId = reader.GetUInt32("bonus_item_id"),
                    BonusItemCount = reader.GetUInt32("bonus_item_count")
                };

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
                var entry = new IcsItem
                {
                    ShopId = reader.GetUInt32("shop_id"),
                    DisplayItemId = reader.GetUInt32("display_item_id"),
                    Name = reader.IsDBNull(reader.GetOrdinal("name")) ? "" : reader.GetString("name"),
                    LimitedType = (CashShopLimitType)reader.GetByte("limited_type"),
                    LimitedStockMax = reader.GetUInt16("limited_stock_max"),
                    LevelMin = reader.GetByte("level_min"),
                    LevelMax = reader.GetByte("level_max"),
                    BuyRestrictType = (CashShopRestrictSaleType)reader.GetByte("buy_restrict_type"),
                    BuyRestrictId = reader.GetUInt32("buy_restrict_id"),
                    IsSale = reader.GetBoolean("is_sale"),
                    IsHidden = reader.GetBoolean("is_hidden"),
                    SaleStart = reader.IsDBNull(reader.GetOrdinal("sale_start")) ? DateTime.MinValue : reader.GetDateTime("sale_start"),
                    SaleEnd = reader.IsDBNull(reader.GetOrdinal("sale_end")) ? DateTime.MinValue : reader.GetDateTime("sale_end"),
                    Remaining = reader.GetInt32("remaining"),
                    ShopButtons = (CashShopCmdUiType)reader.GetByte("shop_buttons")
                };

                if (!ShopItems.TryAdd(entry.ShopId, entry))
                    Logger.Error($"Duplicate ShopItem {entry.ShopId}");
            }
        }

        // Attach SKUs to Shop Items
        foreach (var (key, sku) in SKUs)
        {
            if (ShopItems.TryGetValue(sku.ShopId, out var shopItem))
            {
                if (shopItem.Skus.Count <= 0 && string.IsNullOrWhiteSpace(shopItem.Name))
                {
                    // First Item, grab it's name when needed
                    shopItem.Name = localizationManager.Get("items", "name", sku.ItemId) ?? "???";
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

                var entry = new IcsMenu
                {
                    Id = reader.GetInt64("id"), MainTab = reader.GetByte("main_tab"), SubTab = reader.GetByte("sub_tab"),
                    TabPos = reader.GetUInt16("tab_pos"),
                    ShopItem = shopItem
                };

                // Note that this List should technically always be in order by main, sub and position
                MenuItems.Add(entry);
            }
        }

        // If something didn't load, force close the shop
        if (MenuItems.Count <= 0 || ShopItems.Count <= 0 || SKUs.Count <= 0)
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
        foreach (var character in worldManager.GetAllCharacters())
            character?.SendPacket(new SCICSCheckTimePacket());
    }

    public void SendICSPage(GameConnection connection, byte mainTabId, byte subTabId, ushort page)
    {
        var thisTabItems = MenuItems.Where(t => t.MainTab == mainTabId && t.SubTab == subTabId).ToList();
        var isLimitedTab = mainTabId == 1 && subTabId == 1;
        var itemsPerPage = isLimitedTab ? 4 : 8;
        if (page < 1)
            page = 1;
        var thisPageItems = thisTabItems.Skip(itemsPerPage * (page - 1)).Take(itemsPerPage)
            .Select(t => t.ShopItem)
            .Where(si => si != null)
            .Cast<IcsItem>()
            .ToList();

        // Send both batches, including empty ones, so the client can finish the refresh.
        Logger.Info(
            "ICSGoods push main={0} sub={1} page={2} items={3} skus={4}",
            mainTabId, subTabId, page, thisPageItems.Count,
            thisPageItems.Sum(i => i.Skus.Count));
        connection.SendPacket(new SCICSGoodListPacket(mainTabId, subTabId, thisPageItems));

        var skus = new List<IcsSku>();
        foreach (var shopItem in thisPageItems)
            skus.AddRange(shopItem.Skus.Values);
        connection.SendPacket(new SCICSGoodDetailPacket(skus));
    }

    /// <summary>Push first page for every tab that has listings (client has no CS goods-list request type).</summary>
    public void SendAllIcsTabsFirstPage(GameConnection connection)
    {
        if (!Enabled)
            return;

        var tabs = MenuItems
            .Select(m => (m.MainTab, m.SubTab))
            .Distinct()
            .OrderBy(t => t.MainTab)
            .ThenBy(t => t.SubTab);

        foreach (var (main, sub) in tabs)
            SendICSPage(connection, main, sub, 1);
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

        if ((accountId == 0 && characterId == 0) || shopItemId <= 0)
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
                var entry = new AuditIcsSale
                {
                    BuyerAccount = reader.GetUInt32("buyer_account"),
                    BuyerChar = reader.GetUInt32("buyer_char"),
                    TargetAccount = reader.GetUInt32("target_account"),
                    TargetChar = reader.GetUInt32("target_char"),
                    SaleDate = reader.IsDBNull(reader.GetOrdinal("sale_date")) ? DateTime.MinValue : reader.GetDateTime("sale_date"),
                    ShopItemId = reader.GetUInt32("shop_item_id"),
                    Sku = reader.GetUInt32("sku"), // The SKU Id can be used to get the exact amount of items sold
                    SaleCost = reader.GetInt32("sale_cost"),
                    SaleCurrency = (CashShopCurrencyType)reader.GetByte("sale_currency"),
                    Description = reader.GetString("description")
                };

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

    /// <summary>
    /// How many units of this shop item the buyer already owns under the item's limit scope.
    /// </summary>
    public uint GetPurchasedItemCount(uint accountId, uint characterId, IcsItem shopItem)
    {
        if (shopItem.LimitedType == CashShopLimitType.None)
            return 0;

        var sales = GetSalesForShopItem(
            accountId,
            shopItem.LimitedType == CashShopLimitType.Character ? characterId : 0,
            shopItem.ShopId);

        var count = 0u;
        foreach (var sale in sales)
        {
            if (!SKUs.TryGetValue(sale.Sku, out var sku))
                continue;
            if (shopItem.LimitedType == CashShopLimitType.Character && sale.BuyerChar != characterId)
                continue;
            if (shopItem.LimitedType == CashShopLimitType.Account && sale.BuyerAccount != accountId)
                continue;
            count += sku.ItemCount;
        }

        return count;
    }

    /// <summary>
    /// Entries for SCICSBuyCount (kind 1): every limited ICS product + how many already bought.
    /// Client maps shopIds to list badges (e.g. green "3" sold-out style remaining UX).
    /// </summary>
    public List<(uint ShopId, uint BuyCount)> BuildBuyCountEntries(uint accountId, uint characterId)
    {
        var result = new List<(uint, uint)>();
        foreach (var shop in ShopItems.Values)
        {
            if (shop.LimitedType == CashShopLimitType.None)
                continue;
            result.Add((shop.ShopId, GetPurchasedItemCount(accountId, characterId, shop)));
        }

        return result;
    }

    public void SendBuyCounts(GameConnection connection, uint accountId, uint characterId, uint kind = 1)
    {
        var entries = BuildBuyCountEntries(accountId, characterId);
        if (entries.Count == 0)
            return;
        connection.SendPacket(new SCICSBuyCountPacket(kind, entries));
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
