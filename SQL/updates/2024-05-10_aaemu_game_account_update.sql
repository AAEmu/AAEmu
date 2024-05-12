-- --------------------------------------------
-- Add access level and loyalty to Accounts
-- --------------------------------------------

SET time_zone = '+00:00';

-- Update accounts table with new values
ALTER TABLE `accounts`
	ADD COLUMN `access_level` INT(11) NOT NULL DEFAULT '0' AFTER `account_id`,
	ADD COLUMN `labor` INT(11) NOT NULL DEFAULT '0' AFTER `access_level`,
	ADD COLUMN `loyalty` INT(11) NOT NULL DEFAULT '0' AFTER `credits`,
	ADD COLUMN `last_updated` DATETIME NOT NULL DEFAULT CURRENT_TIME ON UPDATE CURRENT_TIME AFTER `loyalty`,
	ADD COLUMN `last_login` DATETIME NOT NULL DEFAULT CURRENT_TIME AFTER `last_updated`,
	ADD COLUMN `last_labor_tick` DATETIME NOT NULL DEFAULT CURRENT_TIME AFTER `last_login`,
	ADD COLUMN `last_credits_tick` DATETIME NOT NULL DEFAULT CURRENT_TIME AFTER `last_labor_tick`,
	ADD COLUMN `last_loyalty_tick` DATETIME NOT NULL DEFAULT CURRENT_TIME AFTER `last_credits_tick`;

-- Copy highest access_level value from characters into accounts
UPDATE `accounts` a1, (
    SELECT c1.account_id, c1.access_level
    FROM `characters` c1
    WHERE access_level = (
        SELECT MAX(c2.access_level)
        FROM characters c2
        WHERE c1.account_id = c2.account_id AND deleted = 0
    )
    GROUP BY c1.account_id, c1.access_level
) a2
SET a1.access_level = a2.access_level
WHERE a1.account_id = a2.account_id;

-- Copy highest Labor value from characters into accounts
UPDATE `accounts` a1, (
    SELECT c1.account_id, c1.labor_power
    FROM `characters` c1
    WHERE labor_power = (
        SELECT MAX(c2.labor_power)
        FROM characters c2
        WHERE c1.account_id = c2.account_id AND deleted = 0
    )
    GROUP BY c1.account_id, c1.labor_power
) a2
SET a1.labor = a2.labor_power
WHERE a1.account_id = a2.account_id;

-- Copy Loyalty value from all characters into accounts
UPDATE accounts SET loyalty = 0;
UPDATE accounts c1 SET c1.loyalty = c1.loyalty + (
    SELECT SUM(c2.bm_point)
    FROM characters c2
    WHERE c1.account_id = c2.account_id AND deleted = 0
);

-- Remove old unused fields from character
ALTER TABLE `characters`
	DROP COLUMN `labor_power`,
	DROP COLUMN `labor_power_modified`,
	DROP COLUMN `bm_point`;
