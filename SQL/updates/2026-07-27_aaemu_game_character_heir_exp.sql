USE aaemu_game;

-- Heir progression. heir_levels maps a cumulative exp total to a level (0-70) and a step (1-12),
-- so only the total is stored and the level is derived from it the way the premium grade is derived
-- from characters.point. The top requirement is 178,230,921,286, well past a 32-bit column.
ALTER TABLE `characters`
  ADD COLUMN `heir_exp` bigint(20) unsigned NOT NULL DEFAULT '0' AFTER `recoverable_exp`;
