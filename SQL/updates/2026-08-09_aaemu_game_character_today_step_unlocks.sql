USE aaemu_game;

-- Additive for installs that already created character_today_assignments only.
CREATE TABLE IF NOT EXISTS `character_today_step_unlocks` (
  `owner` int unsigned NOT NULL,
  `real_step` int unsigned NOT NULL,
  `unlocked_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`owner`, `real_step`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
