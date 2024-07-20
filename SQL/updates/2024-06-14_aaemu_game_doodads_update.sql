-- --------------------------------------------
-- Add farm type to doodads to work Public Farm
-- --------------------------------------------
ALTER TABLE `doodads`
	ADD COLUMN `farm_type` INT UNSIGNED NULL DEFAULT '0' COMMENT 'farm type for Public Farm' AFTER `data`;
