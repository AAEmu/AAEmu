-- Per-day vendor sales for the item templates that carry a sale limit (items.one_time_sale and
-- items.limited_sale_count). The day the count belongs to is stored next to it, so the allowance
-- comes back by itself when the date moves and a server that was down across midnight does not keep
-- yesterday's spent allowances. Only limited templates get a row; the rest of the catalogue is
-- never counted.
CREATE TABLE IF NOT EXISTS `item_sale_counts` (
  `item_id` INT UNSIGNED NOT NULL COMMENT 'items.id of the template being sold',
  `day_key` DATE NOT NULL COMMENT 'UTC day the count belongs to',
  `sold_count` INT UNSIGNED NOT NULL DEFAULT 0,
  `updated_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`item_id`),
  KEY `idx_item_sale_day` (`day_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Per-day vendor sales of limited item templates';
