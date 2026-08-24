using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.BillServer.Cash;

public sealed class MysqlCashStore(string connectionString) : ICashStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private MySqlConnection Open()
    {
        var c = new MySqlConnection(connectionString);
        c.Open();
        return c;
    }

    public CashWallet GetBalance(ulong accountId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT cash, bonus_cash FROM cash_balance WHERE account_id=@a";
        cmd.Parameters.AddWithValue("@a", (long)accountId);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return new CashWallet(0, 0);
        return new CashWallet(r.GetInt32(0), r.GetInt32(1));
    }

    public CashWallet? Credit(string opId, ulong accountId, int charId, int worldId, int amount, int priceType, string source)
    {
        return Mutate(opId, accountId, charId, worldId, amount, priceType, source, credit: true);
    }

    public CashWallet? Debit(string opId, ulong accountId, int charId, int worldId, int amount, int priceType, string source)
    {
        return Mutate(opId, accountId, charId, worldId, amount, priceType, source, credit: false);
    }

    private CashWallet? Mutate(string opId, ulong accountId, int charId, int worldId, int amount, int priceType, string source, bool credit)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        using (var check = conn.CreateCommand())
        {
            check.Transaction = tx;
            check.CommandText = "SELECT remain_cash, remain_bonus FROM cash_ledger WHERE op_id=@o";
            check.Parameters.AddWithValue("@o", opId);
            using var r = check.ExecuteReader();
            if (r.Read())
            {
                var prior = new CashWallet(r.GetInt32(0), r.GetInt32(1));
                r.Close();
                tx.Commit();
                return prior;
            }
        }

        EnsureRow(conn, tx, accountId);
        int cash, bonus;
        using (var sel = conn.CreateCommand())
        {
            sel.Transaction = tx;
            sel.CommandText = "SELECT cash, bonus_cash FROM cash_balance WHERE account_id=@a FOR UPDATE";
            sel.Parameters.AddWithValue("@a", (long)accountId);
            using var r = sel.ExecuteReader();
            r.Read();
            cash = r.GetInt32(0);
            bonus = r.GetInt32(1);
        }

        if (credit)
        {
            if (priceType == 5) bonus += amount;
            else cash += amount;
        }
        else
        {
            if (priceType == 5)
            {
                if (bonus < amount) { tx.Rollback(); return null; }
                bonus -= amount;
            }
            else
            {
                if (cash < amount) { tx.Rollback(); return null; }
                cash -= amount;
            }
        }

        using (var upd = conn.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = "UPDATE cash_balance SET cash=@c, bonus_cash=@b WHERE account_id=@a";
            upd.Parameters.AddWithValue("@c", cash);
            upd.Parameters.AddWithValue("@b", bonus);
            upd.Parameters.AddWithValue("@a", (long)accountId);
            upd.ExecuteNonQuery();
        }

        using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT INTO cash_ledger
                (op_id, kind, account_id, char_id, world_id, amount, price_type, source, remain_cash, remain_bonus)
                VALUES (@o, @k, @a, @ch, @w, @am, @pt, @s, @rc, @rb)
                """;
            ins.Parameters.AddWithValue("@o", opId);
            ins.Parameters.AddWithValue("@k", credit ? "CREDIT" : "DEBIT");
            ins.Parameters.AddWithValue("@a", (long)accountId);
            ins.Parameters.AddWithValue("@ch", charId);
            ins.Parameters.AddWithValue("@w", worldId);
            ins.Parameters.AddWithValue("@am", amount);
            ins.Parameters.AddWithValue("@pt", priceType);
            ins.Parameters.AddWithValue("@s", source);
            ins.Parameters.AddWithValue("@rc", cash);
            ins.Parameters.AddWithValue("@rb", bonus);
            ins.ExecuteNonQuery();
        }

        tx.Commit();
        return new CashWallet(cash, bonus);
    }

    private static void EnsureRow(MySqlConnection conn, MySqlTransaction tx, ulong accountId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT IGNORE INTO cash_balance (account_id, cash, bonus_cash) VALUES (@a, 0, 0)";
        cmd.Parameters.AddWithValue("@a", (long)accountId);
        cmd.ExecuteNonQuery();
    }

    public int GetBuyCount(ulong accountId, int charId, int productId, int limitType)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM cash_buy_request WHERE account_id=@a AND cash_shop_id=@p";
        cmd.Parameters.AddWithValue("@a", (long)accountId);
        cmd.Parameters.AddWithValue("@p", productId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void RecordBuySlot(long requestId, ulong accountId, int charId, int buySource, int slot, int cashShopId, int priceType, int price, int limitType, int buyLimit, string source)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT IGNORE INTO cash_buy_request
            (request_id, slot, account_id, char_id, buy_source, cash_shop_id, price_type, price, limit_type, buy_limit, source)
            VALUES (@r,@s,@a,@c,@bs,@cs,@pt,@pr,@lt,@bl,@src)
            """;
        cmd.Parameters.AddWithValue("@r", requestId);
        cmd.Parameters.AddWithValue("@s", slot);
        cmd.Parameters.AddWithValue("@a", (long)accountId);
        cmd.Parameters.AddWithValue("@c", charId);
        cmd.Parameters.AddWithValue("@bs", buySource);
        cmd.Parameters.AddWithValue("@cs", cashShopId);
        cmd.Parameters.AddWithValue("@pt", priceType);
        cmd.Parameters.AddWithValue("@pr", price);
        cmd.Parameters.AddWithValue("@lt", limitType);
        cmd.Parameters.AddWithValue("@bl", buyLimit);
        cmd.Parameters.AddWithValue("@src", source);
        cmd.ExecuteNonQuery();
    }

    public void ConfirmBuy(long requestId, int charId, int productId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE cash_buy_request SET confirmed=1 WHERE request_id=@r AND cash_shop_id=@p";
        cmd.Parameters.AddWithValue("@r", requestId);
        cmd.Parameters.AddWithValue("@p", productId);
        cmd.ExecuteNonQuery();
    }
}

public sealed class MysqlCatalogStore(string connectionString) : ICatalogStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private MySqlConnection Open()
    {
        var c = new MySqlConnection(connectionString);
        c.Open();
        return c;
    }

    public IReadOnlyList<ProductDef> ListAll()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT shop_id, sku, item_id, item_count, name, available, price, discount_price, price_type, ics_currency, buy_limit, limit_type, main_tab, sub_tab, tab_pos FROM bill_products ORDER BY shop_id";
        using var r = cmd.ExecuteReader();
        var list = new List<ProductDef>();
        while (r.Read())
        {
            list.Add(new ProductDef(
                r.GetUInt32(0), r.GetUInt32(1), r.GetUInt32(2), r.GetUInt32(3), r.GetString(4),
                r.GetByte(5), r.GetUInt32(6), r.GetUInt32(7), r.GetUInt16(8), r.GetByte(9),
                r.GetUInt32(10), r.GetByte(11), r.GetByte(12), r.GetByte(13), r.GetInt32(14)));
        }

        return list;
    }

    public IReadOnlyList<ProductDef> ListAvailable() => ListAll().Where(p => p.Available != 0).ToList();

    public ProductDef? Get(uint shopId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT shop_id, sku, item_id, item_count, name, available, price, discount_price, price_type, ics_currency, buy_limit, limit_type, main_tab, sub_tab, tab_pos FROM bill_products WHERE shop_id=@i";
        cmd.Parameters.AddWithValue("@i", shopId);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return null;
        return new ProductDef(
            r.GetUInt32(0), r.GetUInt32(1), r.GetUInt32(2), r.GetUInt32(3), r.GetString(4),
            r.GetByte(5), r.GetUInt32(6), r.GetUInt32(7), r.GetUInt16(8), r.GetByte(9),
            r.GetUInt32(10), r.GetByte(11), r.GetByte(12), r.GetByte(13), r.GetInt32(14));
    }

    public void Upsert(ProductDef p)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO bill_products
            (shop_id, sku, item_id, item_count, name, available, price, discount_price, price_type, ics_currency, buy_limit, limit_type, main_tab, sub_tab, tab_pos)
            VALUES
            (@sid,@sku,@item,@cnt,@name,@av,@price,@disc,@pt,@cur,@bl,@lt,@mt,@st,@pos)
            ON DUPLICATE KEY UPDATE
              sku=VALUES(sku), item_id=VALUES(item_id), item_count=VALUES(item_count), name=VALUES(name),
              available=VALUES(available), price=VALUES(price), discount_price=VALUES(discount_price),
              price_type=VALUES(price_type), ics_currency=VALUES(ics_currency), buy_limit=VALUES(buy_limit),
              limit_type=VALUES(limit_type), main_tab=VALUES(main_tab), sub_tab=VALUES(sub_tab), tab_pos=VALUES(tab_pos)
            """;
        cmd.Parameters.AddWithValue("@sid", p.ShopId);
        cmd.Parameters.AddWithValue("@sku", p.Sku);
        cmd.Parameters.AddWithValue("@item", p.ItemId);
        cmd.Parameters.AddWithValue("@cnt", p.ItemCount);
        cmd.Parameters.AddWithValue("@name", p.Name);
        cmd.Parameters.AddWithValue("@av", p.Available);
        cmd.Parameters.AddWithValue("@price", p.Price);
        cmd.Parameters.AddWithValue("@disc", p.DiscountPrice);
        cmd.Parameters.AddWithValue("@pt", p.PriceType);
        cmd.Parameters.AddWithValue("@cur", p.IcsCurrency);
        cmd.Parameters.AddWithValue("@bl", p.BuyLimit);
        cmd.Parameters.AddWithValue("@lt", p.LimitType);
        cmd.Parameters.AddWithValue("@mt", p.MainTab);
        cmd.Parameters.AddWithValue("@st", p.SubTab);
        cmd.Parameters.AddWithValue("@pos", p.TabPos);
        cmd.ExecuteNonQuery();
    }

    public int PublishToIcs(string? gameConnectionString, CompactItemNameCatalog? nameCatalog = null)
    {
        if (string.IsNullOrWhiteSpace(gameConnectionString))
        {
            Log.Warn("PublishToIcs: no game connection string");
            return 0;
        }

        return IcsCatalogPublisher.Publish(gameConnectionString, ListAvailable(), nameCatalog);
    }

    public int FillMissingNames(CompactItemNameCatalog nameCatalog)
    {
        if (!nameCatalog.IsAvailable)
            return 0;

        var updated = 0;
        foreach (var p in ListAll())
        {
            if (!CompactItemNameCatalog.NeedsResolvedName(p.Name))
                continue;

            var resolved = nameCatalog.ResolveDisplayName(p.Name, p.ItemId);
            if (string.IsNullOrWhiteSpace(resolved) || string.Equals(resolved, p.Name, StringComparison.Ordinal))
                continue;

            Upsert(p with { Name = resolved });
            updated++;
        }

        Log.Info("Filled {0} product names from {1}", updated, nameCatalog.CompactPath);
        return updated;
    }
}
