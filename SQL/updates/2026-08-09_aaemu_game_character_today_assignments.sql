USE aaemu_game;

-- Path of Destiny / Daily Contracts per-character state for the current day.
-- day_key resets at local midnight: rows with an older day_key are ignored and replaced.
CREATE TABLE IF NOT EXISTS `character_today_assignments` (
  `owner` int unsigned NOT NULL,
  `real_step` int unsigned NOT NULL,
  `group_id` int unsigned NOT NULL DEFAULT 0,
  `quest_context_id` int unsigned NOT NULL DEFAULT 0,
  `status` tinyint NOT NULL DEFAULT 0 COMMENT '1=Ready 2=Progress 3=Done (A_TODAY_STATUS)',
  `day_key` date NOT NULL,
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`owner`, `real_step`),
  KEY `idx_owner_day` (`owner`, `day_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
