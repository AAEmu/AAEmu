using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.BillServer.Cash;

internal static class IcsCatalogPublisher
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public static int Publish(
        string gameConnectionString,
        IReadOnlyList<ProductDef> available,
        CompactItemNameCatalog? nameCatalog)
    {
        if (available.Count == 0)
            throw new InvalidOperationException("Refusing to publish an empty catalog — that would wipe the in-game cash shop.");

        using var conn = new MySqlConnection(gameConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            using (var clear = conn.CreateCommand())
            {
                clear.Transaction = tx;
                clear.CommandText = """
                    DELETE FROM ics_menu WHERE shop_id >= 2000000 AND shop_id < 3000000;
                    DELETE FROM ics_skus WHERE shop_id >= 2000000 AND shop_id < 3000000;
                    DELETE FROM ics_shop_items WHERE shop_id >= 2000000 AND shop_id < 3000000;
                    """;
                clear.ExecuteNonQuery();
            }

            var n = 0;
            foreach (var p in available)
            {
                if (p.ItemId == 0)
                {
                    Log.Warn("Skipping publish for shopId={0}: itemId is 0", p.ShopId);
                    continue;
                }

                var displayName = nameCatalog?.ResolveDisplayName(p.Name, p.ItemId) ?? p.Name;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        INSERT INTO ics_shop_items
                        (shop_id, display_item_id, name, limited_type, limited_stock_max, level_min, level_max,
                         buy_restrict_type, buy_restrict_id, is_sale, is_hidden, sale_start, sale_end, shop_buttons, remaining)
                        VALUES
                        (@sid, @item, @name, @limType, @limMax, 0, 0, 0, 0, 0, 0, NULL, NULL, 0, -1)
                        """;
                    cmd.Parameters.AddWithValue("@sid", p.ShopId);
                    cmd.Parameters.AddWithValue("@item", p.ItemId);
                    cmd.Parameters.AddWithValue("@name", displayName);
                    cmd.Parameters.AddWithValue("@limType", p.LimitType);
                    cmd.Parameters.AddWithValue("@limMax", p.BuyLimit);
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    var sku = p.Sku != 0 ? p.Sku : p.ShopId + 1000000u;
                    cmd.CommandText = """
                        INSERT INTO ics_skus
                        (sku, shop_id, position, item_id, item_count, select_type, is_default, event_type, event_end_date,
                         currency, price, discount_price, bonus_item_id, bonus_item_count)
                        VALUES
                        (@sku, @sid, 0, @item, @cnt, 0, 1, 0, NULL, @cur, @price, @disc, 0, 0)
                        """;
                    cmd.Parameters.AddWithValue("@sku", sku);
                    cmd.Parameters.AddWithValue("@sid", p.ShopId);
                    cmd.Parameters.AddWithValue("@item", p.ItemId);
                    cmd.Parameters.AddWithValue("@cnt", p.ItemCount);
                    cmd.Parameters.AddWithValue("@cur", p.IcsCurrency);
                    cmd.Parameters.AddWithValue("@price", p.Price);
                    cmd.Parameters.AddWithValue("@disc", p.DiscountPrice);
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "INSERT INTO ics_menu (main_tab, sub_tab, tab_pos, shop_id) VALUES (@mt, @st, @pos, @sid)";
                    cmd.Parameters.AddWithValue("@mt", p.MainTab);
                    cmd.Parameters.AddWithValue("@st", p.SubTab);
                    cmd.Parameters.AddWithValue("@pos", p.TabPos);
                    cmd.Parameters.AddWithValue("@sid", p.ShopId);
                    cmd.ExecuteNonQuery();
                }

                n++;
            }

            if (n == 0)
                throw new InvalidOperationException("No valid products to publish after validation.");

            tx.Commit();

            if (nameCatalog is { IsAvailable: true })
                Log.Info("Published {0} products to ICS (names from {1})", n, nameCatalog.CompactPath);
            else
                Log.Info("Published {0} products to ICS", n);

            return n;
        }
        catch
        {
            try { tx.Rollback(); } catch { /* ignore rollback failure */ }
            throw;
        }
    }
}
