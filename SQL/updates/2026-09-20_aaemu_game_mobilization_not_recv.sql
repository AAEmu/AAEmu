USE aaemu_game;

-- Last time a character checked "do not receive Mobilization Orders today".
-- Mute lasts the rest of that UTC day (HeroElectionRules.IsMobilizationOrderMutedToday).
ALTER TABLE `characters` ADD COLUMN `last_mobilization_not_recv_time` DATETIME NOT NULL DEFAULT '1970-01-01 00:00:00' AFTER `last_mobilization_accept_time`;
