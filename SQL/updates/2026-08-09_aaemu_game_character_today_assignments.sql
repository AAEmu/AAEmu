USE aaemu_game;

-- Daily assignment progress (Ready / Progress / Done for local calendar day).
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

-- Free Change Mission re-rolls used today.
CREATE TABLE IF NOT EXISTS `character_today_reset_counts` (
  `owner` int unsigned NOT NULL,
  `day_key` date NOT NULL,
  `resets_used` tinyint unsigned NOT NULL DEFAULT 0,
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`owner`),
  KEY `idx_day` (`day_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Paid today_quest_steps: unlock once per character (item cost). After that, each day starts Ready.
CREATE TABLE IF NOT EXISTS `character_today_step_unlocks` (
  `owner` int unsigned NOT NULL,
  `real_step` int unsigned NOT NULL,
  `unlocked_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`owner`, `real_step`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
