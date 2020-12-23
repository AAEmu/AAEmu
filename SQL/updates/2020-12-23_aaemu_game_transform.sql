-- -------------------------------------------------
-- Add float rotations
-- -------------------------------------------------
ALTER TABLE `characters` 
ADD COLUMN `yaw` FLOAT NOT NULL DEFAULT '0' AFTER `z`,
ADD COLUMN `pitch` FLOAT NOT NULL DEFAULT '0' AFTER `yaw`,
ADD COLUMN `roll` FLOAT NOT NULL DEFAULT '0' AFTER `pitch`;
