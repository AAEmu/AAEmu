-- --------------------------------------------
-- Add owner type support to persistent slaves
-- --------------------------------------------
ALTER TABLE `slaves`
	ADD COLUMN `template_id` INT(10) UNSIGNED NULL DEFAULT '0' AFTER `item_id` COMMENT 'Slave template Id of this vehicle',
	ADD COLUMN `attach_point` INT(10) NULL DEFAULT '0' COMMENT 'Binding point Id' AFTER `template_id`
	ADD COLUMN `owner_type` INT(10) UNSIGNED NULL DEFAULT '0' COMMENT 'Parent unit type' AFTER `name`,
	ADD COLUMN `owner_id` INT(10) UNSIGNED NULL DEFAULT '0' COMMENT 'Parent unit DB Id' AFTER `owner_type`,
	CHANGE COLUMN `owner` `summoner` INT(10) UNSIGNED NULL DEFAULT NULL COMMENT 'Owning player' AFTER `owner_id`;
	
UPDATE `slaves` SET `owner_id` = `owner`