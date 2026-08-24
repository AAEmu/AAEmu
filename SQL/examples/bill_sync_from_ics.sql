-- Sync Bill Server catalog + test wallets from aaemu_game ICS tables.
-- Run after example-ics-default-en.sql (or any full ics_* seed).

USE aaemu_bill;

-- One bill row per shop: default SKU + first menu placement.
DELETE FROM bill_products WHERE shop_id >= 2000000 AND shop_id < 3000000;

INSERT INTO bill_products
  (shop_id, sku, item_id, item_count, name, available, price, discount_price,
   price_type, ics_currency, buy_limit, limit_type, main_tab, sub_tab, tab_pos)
SELECT
  si.shop_id,
  sk.sku,
  sk.item_id,
  GREATEST(sk.item_count, 1),
  COALESCE(NULLIF(si.name, ''), CONCAT('Premium #', si.shop_id)),
  IF(si.is_hidden = 1, 0, 1),
  sk.price,
  sk.discount_price,
  0,
  sk.currency,
  IF(si.limited_stock_max > 0, si.limited_stock_max, 0),
  si.limited_type,
  m.main_tab,
  m.sub_tab,
  m.tab_pos
FROM aaemu_game.ics_shop_items si
INNER JOIN aaemu_game.ics_skus sk
  ON sk.shop_id = si.shop_id AND sk.is_default = 1
INNER JOIN (
  SELECT shop_id, MIN(id) AS menu_id
  FROM aaemu_game.ics_menu
  WHERE shop_id >= 2000000 AND shop_id < 3000000
  GROUP BY shop_id
) pick ON pick.shop_id = si.shop_id
INNER JOIN aaemu_game.ics_menu m ON m.id = pick.menu_id
WHERE si.shop_id >= 2000000 AND si.shop_id < 3000000;

-- Grant Bill wallets for every game account (live testing).
INSERT INTO cash_balance (account_id, cash, bonus_cash)
SELECT account_id, 50000, 5000 FROM aaemu_game.accounts
ON DUPLICATE KEY UPDATE cash = GREATEST(cash, 50000), bonus_cash = GREATEST(bonus_cash, 5000);

-- Mirror credits in game DB so non-Bill fallback still shows balance.
UPDATE aaemu_game.accounts SET credits = GREATEST(credits, 50000);
