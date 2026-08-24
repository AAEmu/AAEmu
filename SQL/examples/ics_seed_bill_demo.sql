-- Minimal ICS seed (demo) matching BillServer default catalog — safe for local.
-- After load: /ics reload (shop off first) or restart World.

USE aaemu_game;

DELETE FROM ics_menu WHERE shop_id >= 2000000 AND shop_id < 3000000;
DELETE FROM ics_skus WHERE shop_id >= 2000000 AND shop_id < 3000000;
DELETE FROM ics_shop_items WHERE shop_id >= 2000000 AND shop_id < 3000000;

INSERT INTO ics_shop_items
(shop_id, display_item_id, name, limited_type, limited_stock_max, level_min, level_max,
 buy_restrict_type, buy_restrict_id, is_sale, is_hidden, sale_start, sale_end, shop_buttons, remaining)
VALUES
(2000000, 29176, 'Starter Pack Credit Test', 0, 0, 0, 0, 0, 0, 0, 0, NULL, NULL, 0, -1),
(2000001, 29177, 'Limited Mount Coupon', 1, 3, 0, 0, 0, 0, 0, 0, NULL, NULL, 0, -1);

INSERT INTO ics_skus
(sku, shop_id, position, item_id, item_count, select_type, is_default, event_type, event_end_date,
 currency, price, discount_price, bonus_item_id, bonus_item_count)
VALUES
(1000000, 2000000, 0, 29176, 1, 0, 1, 0, NULL, 0, 100, 0, 0, 0),
(1000001, 2000001, 0, 29177, 1, 0, 1, 0, NULL, 0, 500, 400, 0, 0);

INSERT INTO ics_menu (main_tab, sub_tab, tab_pos, shop_id) VALUES
(1, 1, 0, 2000000),
(1, 1, 1, 2000001);
