-- --------------------------------------------
-- Add Jury Points to character
-- --------------------------------------------
ALTER TABLE `characters`
	ADD COLUMN `jury_point` INT(11) NOT NULL DEFAULT '0' AFTER `crime_record`;
	