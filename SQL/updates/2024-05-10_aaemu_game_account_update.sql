-- --------------------------------------------
-- Add access level and loyalty to Accounts
-- NOTE: Loyalty and Labor update: Make sure to compensate your users for lost Loyalty and Labor as a result of this update
-- --------------------------------------------
ALTER TABLE `accounts`
	ADD COLUMN `access_level` INT(11) NOT NULL DEFAULT '0' AFTER `account_id`,
	ADD COLUMN `labor` INT(11) NOT NULL DEFAULT '0' AFTER `access_level`,
	ADD COLUMN `loyalty` INT(11) NOT NULL DEFAULT '0' AFTER `credits`,
	ADD COLUMN `last_updated` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP AFTER `loyalty`,
	ADD COLUMN `last_login` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP AFTER `last_updated`,
	ADD COLUMN `last_labor_tick` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP AFTER `last_login`,
	ADD COLUMN `last_credits_tick` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP AFTER `last_labor_tick`,
	ADD COLUMN `last_loyalty_tick` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP AFTER `last_credits_tick`;

-- Possibly add query to update/copy values

ALTER TABLE `characters`
	DROP COLUMN `labor_power`,
	DROP COLUMN `labor_power_modified`,
	DROP COLUMN `bm_point`;
