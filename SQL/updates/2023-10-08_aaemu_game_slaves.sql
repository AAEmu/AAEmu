-- -------------------------------------------------
-- Add slaves table
-- Doodad add attach_point field
-- -------------------------------------------------

CREATE TABLE `slaves` (
	`id` INT(10) UNSIGNED NOT NULL,
	`item_id` INT(10) UNSIGNED NULL DEFAULT NULL COMMENT 'Item that is used to summon this vehicle',
	`name` TEXT NULL DEFAULT NULL COLLATE 'utf8mb4_general_ci',
	`owner` INT(10) UNSIGNED NULL DEFAULT NULL,
	`created_at` DATETIME NULL DEFAULT CURRENT_TIMESTAMP,
	`updated_at` DATETIME NULL DEFAULT CURRENT_TIMESTAMP,
	`hp` INT(11) NULL DEFAULT NULL,
	`mp` INT(11) NULL DEFAULT NULL,
	`x` FLOAT NULL DEFAULT NULL,
	`y` FLOAT NULL DEFAULT NULL,
	`z` FLOAT NULL DEFAULT NULL,
	PRIMARY KEY (`id`) USING BTREE
) COMMENT='Player vehicles summons' COLLATE 'utf8mb4_general_ci' ENGINE=InnoDB;

ALTER TABLE `doodads` ADD COLUMN `attach_point` INT(11) UNSIGNED NULL DEFAULT '0' COMMENT 'Slot this doodad fits in on the owner' AFTER `owner_type`;
ALTER TABLE `doodads` ADD COLUMN `scale` FLOAT NOT NULL DEFAULT '1'  AFTER `yaw`;
