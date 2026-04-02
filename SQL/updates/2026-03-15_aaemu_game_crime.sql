-- ------------------------------------------------
-- Add table to keep track of in-game crime events
-- ------------------------------------------------

CREATE TABLE IF NOT EXISTS `crime` (
	`id` INT UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Crime point Id',
	`criminal` INT UNSIGNED NULL DEFAULT NULL COMMENT 'Player Id of the criminal',
	`victim` INT UNSIGNED NULL DEFAULT NULL COMMENT 'Player Id of the victim',
	`reporter` INT UNSIGNED NULL DEFAULT NULL COMMENT 'Player Id of the reporter',
	`crime_type` INT UNSIGNED NULL DEFAULT NULL COMMENT 'Crime Type Id',
	`doodad_template` INT UNSIGNED NULL DEFAULT NULL COMMENT 'Related doodad template',
	`zone_key` INT UNSIGNED NULL DEFAULT NULL COMMENT 'Zone group Id of where the crime happened',
	`x` FLOAT NULL DEFAULT '0',
	`y` FLOAT NULL DEFAULT '0',
	`z` FLOAT NULL DEFAULT '0',
	`crime_time` DATETIME NULL DEFAULT NULL,
	`report_time` DATETIME NULL DEFAULT NULL,
	`arg1` INT UNSIGNED NULL DEFAULT NULL COMMENT 'Argument1 of reported crime',
	`arg2` INT UNSIGNED NULL DEFAULT NULL COMMENT 'Argument2 of reported crime',
	`arg3` INT UNSIGNED NULL DEFAULT NULL COMMENT 'Argument3 of reported crime',
	`msg` TEXT NULL DEFAULT NULL COLLATE 'utf8mb4_general_ci',
	PRIMARY KEY (`id`) USING BTREE
)
COMMENT='Keeps track of the crime events'
COLLATE='utf8mb4_general_ci'
ENGINE=InnoDB
;