-- 1970-01-05 is the first Monday after the Unix epoch. Existing rows therefore roll into
-- the server's current Monday-based period on first load.
ALTER TABLE `expedition_members`
    ADD COLUMN `weekly_contribution_period_start` date NOT NULL DEFAULT '1970-01-05'
        AFTER `weekly_contribution_point`;
