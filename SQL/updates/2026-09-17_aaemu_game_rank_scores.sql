USE aaemu_game;

CREATE TABLE IF NOT EXISTS `character_rank_scores` (
  `rank_id` int unsigned NOT NULL COMMENT 'Board id from ranks',
  `holder_kind` tinyint unsigned NOT NULL COMMENT '0 character, 1 expedition',
  `holder_id` bigint unsigned NOT NULL COMMENT 'Character id or expedition id',
  `period_start` datetime NOT NULL COMMENT 'The window the value was counted in',
  `account_id` int unsigned NOT NULL DEFAULT 0,
  `world_id` tinyint unsigned NOT NULL DEFAULT 0 COMMENT 'The server the holder was last seen on',
  `value` bigint NOT NULL DEFAULT 0 COMMENT 'The figure the board orders by',
  `bare_value` bigint NOT NULL DEFAULT 0 COMMENT 'The second figure the window shows',
  `updated_at` datetime NOT NULL COMMENT 'When the value was last written',
  PRIMARY KEY (`rank_id`,`holder_kind`,`holder_id`,`period_start`) USING BTREE,
  KEY `ix_rank_scores_board` (`rank_id`,`period_start`,`value`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Ranking board values, one row per holder per board per window; read by the ranking window so offline holders are on the board';
