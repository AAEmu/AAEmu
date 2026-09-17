USE aaemu_game;

CREATE TABLE IF NOT EXISTS `rank_period_payouts` (
  `rank_id` int unsigned NOT NULL COMMENT 'Board whose window was paid',
  `period_start` datetime NOT NULL COMMENT 'The window that was paid out',
  `paid_at` datetime NOT NULL COMMENT 'When it was paid, so a restart does not pay it twice',
  PRIMARY KEY (`rank_id`,`period_start`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Ranking windows that have been paid out; the standings themselves stay in character_rank_scores';
