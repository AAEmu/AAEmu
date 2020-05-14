ALTER TABLE `aaemu_game.items` DROP COLUMN `bounded`;
ALTER TABLE `aaemu_game.items` ADD `flags` UNSIGNED tinyint(3) NOT NULL AFTER `grade`;