CREATE TABLE IF NOT EXISTS `expedition_instance_histories` (
  `history_id` bigint unsigned NOT NULL AUTO_INCREMENT,
  `expedition_id` int NOT NULL,
  `battlefield_type` int unsigned NOT NULL,
  `instance_id` int unsigned NOT NULL,
  `score` int unsigned NOT NULL,
  `play_result` tinyint unsigned NOT NULL,
  `recorded_at` datetime(6) NOT NULL,
  PRIMARY KEY (`history_id`),
  KEY `idx_expedition_instance_history` (`expedition_id`,`recorded_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Guild battlefield result history';

CREATE TABLE IF NOT EXISTS `expedition_instance_history_members` (
  `history_id` bigint unsigned NOT NULL,
  `character_id` bigint unsigned NOT NULL,
  `status` tinyint unsigned NOT NULL,
  PRIMARY KEY (`history_id`,`character_id`),
  KEY `idx_expedition_instance_history_member_character` (`character_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Guild battlefield result participants';
