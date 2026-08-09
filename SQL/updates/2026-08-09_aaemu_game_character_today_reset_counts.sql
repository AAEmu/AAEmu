USE aaemu_game;

-- Additive table for installs that already applied character_today_assignments only.
CREATE TABLE IF NOT EXISTS `character_today_reset_counts` (
  `owner` int unsigned NOT NULL,
  `day_key` date NOT NULL,
  `resets_used` tinyint unsigned NOT NULL DEFAULT 0,
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`owner`),
  KEY `idx_day` (`day_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
