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
ADD `house_id` int unsigned NOT NULL DEFAULT '0' AFTER `item_id`,
ADD `parent_doodad` int unsigned NOT NULL DEFAULT '0' AFTER `house_id`,
ADD `item_template_id` int unsigned NOT NULL DEFAULT '0' AFTER `parent_doodad`;

-- -------------------------------------------------
-- Change ID to unsigned
-- -------------------------------------------------
ALTER TABLE `doodads`
CHANGE `id` `id` int unsigned NOT NULL AUTO_INCREMENT FIRST ;
