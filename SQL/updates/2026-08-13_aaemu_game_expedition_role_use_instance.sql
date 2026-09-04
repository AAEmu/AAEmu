USE aaemu_game;

ALTER TABLE `expedition_role_policies` ADD COLUMN `use_instance` TINYINT(1) NOT NULL DEFAULT '0' AFTER `join_siege`;
