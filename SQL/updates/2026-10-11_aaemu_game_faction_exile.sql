USE aaemu_game;

-- Exile system state and GM overrides, plus GM-forced faction power standings.

CREATE TABLE IF NOT EXISTS `faction_exile_settings` (
  `name` varchar(64) NOT NULL,
  `value` int NOT NULL DEFAULT '0',
  PRIMARY KEY (`name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Exile system switches';

CREATE TABLE IF NOT EXISTS `faction_exile_routes` (
  `from_faction` int unsigned NOT NULL,
  `to_faction` int unsigned NOT NULL,
  `mode` tinyint unsigned NOT NULL DEFAULT '0' COMMENT '0 automatic, 1 forced open, 2 forced closed',
  `max_override` int DEFAULT NULL COMMENT 'weekly limit set by a GM; NULL = from faction_change_limit_nums',
  PRIMARY KEY (`from_faction`,`to_faction`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='GM overrides per exile route';

CREATE TABLE IF NOT EXISTS `faction_exile_counts` (
  `week_num` int unsigned NOT NULL,
  `from_faction` int unsigned NOT NULL,
  `to_faction` int unsigned NOT NULL,
  `used` int unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`week_num`,`from_faction`,`to_faction`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Exiles taken per route and week';

CREATE TABLE IF NOT EXISTS `faction_exile_suspensions` (
  `faction_id` int unsigned NOT NULL,
  PRIMARY KEY (`faction_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Factions taken out of the exile system by a GM';

CREATE TABLE IF NOT EXISTS `character_exiles` (
  `character_id` int unsigned NOT NULL,
  `original_faction_id` int unsigned NOT NULL COMMENT 'faction before the first exile, for returning home',
  `from_faction_id` int unsigned NOT NULL,
  `to_faction_id` int unsigned NOT NULL,
  `exiled_at` datetime NOT NULL,
  PRIMARY KEY (`character_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Last exile per character (cooldown and return)';

CREATE TABLE IF NOT EXISTS `faction_power_overrides` (
  `faction_id` int unsigned NOT NULL,
  `status` tinyint unsigned NOT NULL COMMENT '1 superior, 2 inferior, 3 normal',
  PRIMARY KEY (`faction_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='GM-forced faction power standing';
