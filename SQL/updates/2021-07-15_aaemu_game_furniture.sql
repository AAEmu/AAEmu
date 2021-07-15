-- -------------------------------------------------
-- Add recovery flag for housing
-- -------------------------------------------------
ALTER TABLE `housings`
ADD `allow_recover` tinyint unsigned NOT NULL DEFAULT '1';

-- -------------------------------------------------
-- Add relevant entries to doodads
-- -------------------------------------------------
ALTER TABLE `doodads`
ADD `item_id` bigint unsigned NOT NULL DEFAULT '0',
ADD `house_id` int NOT NULL DEFAULT '0' AFTER `item_id`;
