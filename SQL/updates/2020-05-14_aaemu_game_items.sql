ALTER TABLE `items` DROP COLUMN `bounded`;
ALTER TABLE `items` ADD `flags` tinyint(3) unsigned NOT NULL AFTER `grade`;