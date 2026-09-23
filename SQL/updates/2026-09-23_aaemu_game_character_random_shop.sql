-- Per-character random merchant windows. The client reads its OWN refresh counters
-- (X2Store GetRandomShopStoreRefreshCount -> freeCnt/freeMax, chargeCnt/chargeMax) and its paid
-- refresh checks the viewer's inventory, so window state is keyed by character and pack - a
-- shared window would let one character's payment re-roll everyone's stock. period_start marks
-- the UTC day the window was rolled for, so an offline day rollover resets on next use
-- (same lazy-reset shape as character_merchant_purchases).

CREATE TABLE IF NOT EXISTS `character_random_shop_windows` (
  `character_id` INT UNSIGNED NOT NULL,
  `pack_id` INT UNSIGNED NOT NULL COMMENT 'merchant_random_packs.id',
  `period_start` DATETIME NOT NULL COMMENT 'UTC midnight of the day this window was rolled for',
  `rolled_at` DATETIME NOT NULL COMMENT 'When the current offers were rolled (shopDisplayInfo.recordTime)',
  `free_used` INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Free refreshes spent this period (pack refresh_free_cnt is the max)',
  `charge_used` INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Paid refreshes spent this period (pack refresh_charge_cnt is the max)',
  PRIMARY KEY (`character_id`, `pack_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Per-character random merchant window state';

CREATE TABLE IF NOT EXISTS `character_random_shop_offers` (
  `character_id` INT UNSIGNED NOT NULL,
  `pack_id` INT UNSIGNED NOT NULL,
  `slot` INT UNSIGNED NOT NULL COMMENT 'Display slot inside the window (wire display element order field)',
  `group_id` INT UNSIGNED NOT NULL COMMENT 'merchant_random_groups.id',
  `good_id` INT UNSIGNED NOT NULL COMMENT 'merchant_random_goods.id',
  `item_id` INT UNSIGNED NOT NULL COMMENT 'Item template sold by this offer',
  `grade` TINYINT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Grade resolved at load, snapshot for the quote',
  `cost` INT NOT NULL COMMENT 'Price snapshot taken at roll time from merchant_random_goods.cost',
  `currency` TINYINT UNSIGNED NOT NULL COMMENT 'ShopCurrencyType of the offer',
  `sold` TINYINT(1) NOT NULL DEFAULT 0 COMMENT 'Sold exactly once per window: the claim flips 0 -> 1',
  PRIMARY KEY (`character_id`, `pack_id`, `slot`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Per-character random merchant window offers';
