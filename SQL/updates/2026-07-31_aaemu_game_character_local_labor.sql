USE aaemu_game;

-- 10.0.2.13 serializes local labor as a distinct per-character u32 pool. Account labor remains
-- in accounts.labor; this balance backs UnitReq kind 99 and AddLaborPower pool selector 1.
ALTER TABLE `characters`
  ADD COLUMN `local_lp` INT UNSIGNED NOT NULL DEFAULT 0 AFTER `consumed_lp`;
