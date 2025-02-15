-- --------------------------------------------
-- Add table to keep track of cheaters
-- --------------------------------------------

CREATE TABLE `audit_char_sus` (
	`id` BIGINT(20) UNSIGNED NOT NULL AUTO_INCREMENT,
	`sus_date` DATETIME NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Time of incident',
	`sus_category` VARCHAR(64) NULL DEFAULT 'None' COMMENT 'Category name for the activity' COLLATE 'utf8mb4_general_ci',
	`sus_account` INT(10) UNSIGNED NULL DEFAULT '0' COMMENT 'Involved account Id (if any)',
	`sus_character` INT(10) UNSIGNED NULL DEFAULT '0' COMMENT 'Involved character Id (if any)',
	`zone_group` INT UNSIGNED NULL DEFAULT '0',
	`x` FLOAT NULL DEFAULT '0',
	`y` FLOAT NULL DEFAULT '0',
	`z` FLOAT NULL DEFAULT '0',
	`description` TEXT NULL COMMENT 'Description of the incident' COLLATE 'utf8mb4_general_ci',
	PRIMARY KEY (`id`) USING BTREE,
	INDEX `sus_date` (`sus_date`),
	INDEX `sus_account` (`sus_account`),
	INDEX `sus_character` (`sus_character`),
	INDEX `sus_category` (`sus_category`)
)
COLLATE='utf8mb4_general_ci'
ENGINE=InnoDB
;
