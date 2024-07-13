-- Update expeditions table with new values
ALTER TABLE `expeditions`
	ADD COLUMN `exp` INT NOT NULL DEFAULT '0' AFTER `level`,
	ADD COLUMN `protect_time` DATETIME  NOT NULL DEFAULT '0001-01-01 00:00:00' AFTER `exp`,
	ADD COLUMN `war_deposit` INT NOT NULL DEFAULT '0' AFTER `protect_time`,
	ADD COLUMN `daily_exp` INT NOT NULL DEFAULT '0' AFTER `war_deposit`,
	ADD COLUMN `last_exp_update_time` DATETIME NOT NULL DEFAULT '0001-01-01 00:00:00' AFTER `daily_exp`,
	ADD COLUMN `is_level_update` TINYINT(1) NOT NULL DEFAULT '0' AFTER `last_exp_update_time`,
	ADD COLUMN `interest` INT NOT NULL DEFAULT '0' AFTER `is_level_update`,
	ADD COLUMN `motd_title` varchar(128) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL AFTER `interest`,
	ADD COLUMN `motd_content` varchar(255) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL AFTER `motd_title`,
	ADD COLUMN `win` INT NOT NULL DEFAULT '0' AFTER `motd_content`,
	ADD COLUMN `lose` INT NOT NULL DEFAULT '0' AFTER `win`,
	ADD COLUMN `draw` INT NOT NULL DEFAULT '0' AFTER `lose`;
