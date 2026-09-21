USE aaemu_game;

-- Last time a character accepted a Mobilization Order. One accept per UTC clock hour
-- (HeroElectionRules.CanAcceptMobilizationAgainThisHour). Issue counters already live on
-- mobilization_order_today_count / last_mobilization_order_time.
ALTER TABLE `characters` ADD COLUMN `last_mobilization_accept_time` DATETIME NOT NULL DEFAULT '1970-01-01 00:00:00' AFTER `last_mobilization_order_time`;
