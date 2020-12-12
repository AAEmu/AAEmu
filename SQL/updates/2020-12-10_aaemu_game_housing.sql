-- -------------------------------------------------
-- Add placing and demolish timers to housing table
-- -------------------------------------------------
ALTER TABLE `housings` 
ADD COLUMN `place_date` DATETIME NOT NULL DEFAULT NOW() AFTER `permission`,
ADD COLUMN `protected_until` DATETIME NOT NULL DEFAULT NOW() AFTER `place_date`,
ADD COLUMN `faction_id` INT UNSIGNED NOT NULL DEFAULT 1 AFTER `protected_until`,
ADD COLUMN `sell_to` INT(10) UNSIGNED NOT NULL DEFAULT 0 AFTER `faction_id`,
ADD COLUMN `sell_price` BIGINT(20) NOT NULL DEFAULT 0 AFTER `sell_to`;

ALTER TABLE `mails`
CHANGE COLUMN `extra` `extra` BIGINT(20) NOT NULL ;
