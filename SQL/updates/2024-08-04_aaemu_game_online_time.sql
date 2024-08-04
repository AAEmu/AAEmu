-- --------------------------------------------
-- Add Online tracking
-- --------------------------------------------
ALTER TABLE `characters`
	ADD COLUMN `online_time` INT(11) NOT NULL DEFAULT '0' COMMENT 'Time that the character has been online' AFTER `return_district`;	
	
ALTER TABLE `accounts`
	ADD COLUMN `divine_clock_time` INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Time that has been passed already' AFTER `last_loyalty_tick`,
	ADD COLUMN `divine_clock_taken` INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Number of clicks taken today' AFTER `divine_clock_time`;
