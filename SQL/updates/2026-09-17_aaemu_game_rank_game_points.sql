USE aaemu_game;

CREATE TABLE IF NOT EXISTS `character_game_point_totals` (
  `character_id` int unsigned NOT NULL COMMENT 'Character id',
  `point_kind` tinyint unsigned NOT NULL COMMENT 'game_point_rank_details.game_point_kind: 0 experience, 1 honor, 2 living point, 3 labour',
  `point_method` tinyint unsigned NOT NULL COMMENT 'game_point_rank_details.game_point_method: 0 gained, 1 spent',
  `period_start` datetime NOT NULL COMMENT 'The ranking window the total is counted in',
  `account_id` int unsigned NOT NULL DEFAULT 0 COMMENT 'The holder''s account, so the board can name them',
  `world_id` tinyint unsigned NOT NULL DEFAULT 0 COMMENT 'The server the holder was last seen on',
  `total` bigint NOT NULL DEFAULT 0 COMMENT 'The amount gained or spent in that window',
  `updated_at` datetime NOT NULL COMMENT 'When the total was last written',
  PRIMARY KEY (`character_id`,`point_kind`,`point_method`,`period_start`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='What a character gained or spent in a ranking window, for the boards that rank a period''s total';
