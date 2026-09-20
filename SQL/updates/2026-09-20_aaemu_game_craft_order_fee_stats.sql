USE aaemu_game;

-- Recent listing-fee range per craft. The post dialog's lowest / highest lines read this
-- after a listing has already left craft_orders (filled, cancelled, or expired).

CREATE TABLE IF NOT EXISTS `craft_order_fee_stats` (
  `craft_id` INT UNSIGNED NOT NULL,
  `lowest` BIGINT UNSIGNED NOT NULL,
  `highest` BIGINT UNSIGNED NOT NULL,
  PRIMARY KEY (`craft_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Recent craft-order listing fee range per craft';
