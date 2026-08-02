-- Persistent 10.0 merchant purchase limits. The client state map is keyed by item template type and
-- carries buyCount plus purchaseType; period_start lets an offline reset be recognized on next use.

CREATE TABLE IF NOT EXISTS `character_merchant_purchases` (
  `character_id` INT UNSIGNED NOT NULL,
  `item_id` INT UNSIGNED NOT NULL COMMENT 'Item template type used as the native client map key',
  `buy_count` INT UNSIGNED NOT NULL DEFAULT 0,
  `purchase_type` TINYINT UNSIGNED NOT NULL COMMENT '1 always, 2 daily, 3 weekly, 4 monthly',
  `period_start` DATETIME NOT NULL COMMENT 'UTC start of the active limit period',
  PRIMARY KEY (`character_id`, `item_id`),
  INDEX `idx_merchant_purchase_type` (`purchase_type`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Persistent per-character merchant purchase limits';
