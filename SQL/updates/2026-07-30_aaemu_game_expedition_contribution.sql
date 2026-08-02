USE aaemu_game;

-- 10.0 expedition shops spend a member-owned contribution wallet. The member descriptor sends
-- both the current balance and the positive contribution accumulated during the weekly period.
ALTER TABLE `expedition_members`
  ADD COLUMN `contribution_point` int unsigned NOT NULL DEFAULT '0' AFTER `memo`,
  ADD COLUMN `weekly_contribution_point` int unsigned NOT NULL DEFAULT '0' AFTER `contribution_point`;
