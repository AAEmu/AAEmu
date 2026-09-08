USE aaemu_game;

CREATE TABLE IF NOT EXISTS `specialty_market_revision` (
  `id` tinyint unsigned NOT NULL,
  `revision` bigint NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  CHECK (`id` = 1),
  CHECK (`revision` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

INSERT INTO `specialty_market_revision` (`id`, `revision`) VALUES (1, 0)
ON DUPLICATE KEY UPDATE `id` = `id`;

CREATE TABLE IF NOT EXISTS `specialty_market_routes` (
  `item_id` int unsigned NOT NULL,
  `zone_group_id` int unsigned NOT NULL,
  `ratio` int NOT NULL,
  `demand_remainder` int NOT NULL DEFAULT 0,
  PRIMARY KEY (`item_id`, `zone_group_id`),
  CHECK (`item_id` > 0 AND `zone_group_id` > 0),
  CHECK (`ratio` >= 0 AND `demand_remainder` BETWEEN 0 AND 3)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

CREATE TABLE IF NOT EXISTS `specialty_market_contributions` (
  `zone_group_id` int unsigned NOT NULL,
  `tag_id` int unsigned NOT NULL,
  `sequence` bigint unsigned NOT NULL,
  `item_id` int unsigned NOT NULL,
  `amount` int unsigned NOT NULL,
  PRIMARY KEY (`zone_group_id`, `tag_id`, `sequence`),
  CHECK (`zone_group_id` > 0 AND `tag_id` > 0),
  CHECK (`sequence` > 0 AND `item_id` > 0 AND `amount` > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

CREATE TABLE IF NOT EXISTS `specialty_market_cargo` (
  `zone_group_id` int unsigned NOT NULL,
  `trade_good_id` int unsigned NOT NULL,
  `amount` int unsigned NOT NULL,
  PRIMARY KEY (`zone_group_id`, `trade_good_id`),
  CHECK (`zone_group_id` > 0 AND `trade_good_id` > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

CREATE TABLE IF NOT EXISTS `specialty_market_history` (
  `item_id` int unsigned NOT NULL,
  `zone_group_id` int unsigned NOT NULL,
  `sequence` tinyint unsigned NOT NULL,
  `ratio` int NOT NULL,
  `recorded` bigint NOT NULL,
  PRIMARY KEY (`item_id`, `zone_group_id`, `sequence`),
  CHECK (`item_id` > 0 AND `zone_group_id` > 0),
  CHECK (`ratio` >= 0 AND `recorded` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;
