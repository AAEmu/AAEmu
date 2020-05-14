ALTER TABLE `aaemu_game.items` DROP COLUMN `bounded`;
ALTER TABLE `aaemu_game.items` ADD `flags` tinyint(3) unsigned NOT NULL AFTER `grade`;