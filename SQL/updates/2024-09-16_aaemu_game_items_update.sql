-- --------------------------------------------
-- Update items table
-- --------------------------------------------
ALTER TABLE items
	DROP COLUMN slot_type;
ALTER TABLE items
	ADD COLUMN `slot_type` INT(11) NOT NULL DEFAULT '0' COMMENT 'Internal Container Type' AFTER `container_id`;
