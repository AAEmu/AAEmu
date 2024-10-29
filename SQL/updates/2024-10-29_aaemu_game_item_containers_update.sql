-- --------------------------------------------
-- Update item_containers table with
-- --------------------------------------------
ALTER TABLE item_containers
	DROP COLUMN slot_type;
ALTER TABLE item_containers
	ADD COLUMN `slot_type` INT(11) NOT NULL DEFAULT '0' COMMENT 'Internal Container Type' AFTER `container_type`;
	