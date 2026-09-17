USE aaemu_game;

CREATE TABLE IF NOT EXISTS `character_rank_records` (
  `character_id` int unsigned NOT NULL COMMENT 'Character the record belongs to',
  `record_kind` tinyint unsigned NOT NULL COMMENT 'RankRecordKind: 1 the longest fish of the window, 2 what the window''s fish weighed together',
  `period_start` datetime NOT NULL COMMENT 'The ranking window the record is counted in',
  `value` bigint NOT NULL DEFAULT 0 COMMENT 'The figure the board ranks: the best of the window, or its total',
  `recorded_at` datetime NOT NULL COMMENT 'When the figure was recorded, which is what the window shows for a catch',
  `account_id` int unsigned NOT NULL DEFAULT 0 COMMENT 'The holder''s account, so the board can name them',
  `world_id` tinyint unsigned NOT NULL DEFAULT 0 COMMENT 'The server the holder was last seen on',
  `updated_at` datetime NOT NULL COMMENT 'When the record was last written',
  PRIMARY KEY (`character_id`,`record_kind`,`period_start`) USING BTREE,
  KEY `ix_rank_records_board` (`record_kind`,`period_start`,`value`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='What a character caught or handed in during a ranking window, for the boards that rank a record rather than a figure held now';
