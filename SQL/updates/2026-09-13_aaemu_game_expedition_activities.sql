CREATE TABLE IF NOT EXISTS `expedition_portals` (
  `id` int unsigned NOT NULL AUTO_INCREMENT,
  `expedition_id` int NOT NULL,
  `name` varchar(128) NOT NULL,
  `zone_id` int unsigned NOT NULL,
  `x` float NOT NULL,
  `y` float NOT NULL,
  `z` float NOT NULL,
  `z_rot` float NOT NULL,
  PRIMARY KEY (`id`),
  KEY `idx_expedition_portals_expedition` (`expedition_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Saved guild portal destinations';

CREATE TABLE IF NOT EXISTS `expedition_management_histories` (
  `id` bigint unsigned NOT NULL AUTO_INCREMENT,
  `expedition_id` int NOT NULL,
  `member_name` varchar(128) NOT NULL,
  `history_type` int NOT NULL,
  `amount` bigint unsigned NOT NULL DEFAULT '0',
  `used_at` datetime(6) NOT NULL,
  `detail_id` int unsigned NOT NULL DEFAULT '0',
  `detail_value` int NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`),
  KEY `idx_expedition_management_history` (`expedition_id`,`used_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Guild management activity history';

CREATE TABLE IF NOT EXISTS `expedition_shop_histories` (
  `id` bigint unsigned NOT NULL AUTO_INCREMENT,
  `expedition_id` int NOT NULL,
  `member_name` varchar(128) NOT NULL,
  `item_id` int NOT NULL,
  `stack` int NOT NULL,
  `amount` bigint unsigned NOT NULL DEFAULT '0',
  `purchased_at` datetime(6) NOT NULL,
  PRIMARY KEY (`id`),
  KEY `idx_expedition_shop_history` (`expedition_id`,`purchased_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Guild shop purchase history';

CREATE TABLE IF NOT EXISTS `expedition_war_histories` (
  `id` bigint unsigned NOT NULL AUTO_INCREMENT,
  `declarer_id` int NOT NULL,
  `declarer_name` varchar(128) NOT NULL,
  `defendant_id` int NOT NULL,
  `defendant_name` varchar(128) NOT NULL,
  `declared_at` datetime(6) NOT NULL,
  `declarer_kills` int unsigned NOT NULL DEFAULT '0',
  `defendant_kills` int unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`),
  KEY `idx_expedition_war_declarer` (`declarer_id`,`declared_at`),
  KEY `idx_expedition_war_defendant` (`defendant_id`,`declared_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Retained guild war history';

CREATE TABLE IF NOT EXISTS `expedition_daily_activity` (
  `expedition_id` int NOT NULL,
  `character_id` int unsigned NOT NULL,
  `period_start` datetime(6) NOT NULL,
  `contribution_used` int NOT NULL DEFAULT '0',
  PRIMARY KEY (`expedition_id`,`character_id`,`period_start`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Guild daily activity counters';
