USE aaemu_game;

-- Live community craft-order board. Compact has no order table; these rows are the
-- listings themselves (craft, fee, owner, listing lifetime). Sheets already persist on items.

CREATE TABLE IF NOT EXISTS `craft_orders` (
  `id` BIGINT UNSIGNED NOT NULL,
  `owner_id` INT UNSIGNED NOT NULL,
  `owner_name` VARCHAR(128) NOT NULL,
  `owner_world_char_key` BIGINT UNSIGNED NOT NULL,
  `craft_id` INT UNSIGNED NOT NULL,
  `item_id` INT UNSIGNED NOT NULL,
  `grade` TINYINT UNSIGNED NOT NULL,
  `count` INT UNSIGNED NOT NULL,
  `fee` BIGINT UNSIGNED NOT NULL,
  `actability_group_id` INT UNSIGNED NOT NULL,
  `actability_point` INT UNSIGNED NOT NULL,
  `posted_unix` BIGINT NOT NULL,
  `expires_unix` BIGINT NOT NULL,
  `status` TINYINT UNSIGNED NOT NULL DEFAULT 0,
  `kind` TINYINT UNSIGNED NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  KEY `idx_craft_orders_owner` (`owner_id`),
  KEY `idx_craft_orders_expires` (`expires_unix`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Live community craft-order board rows';
