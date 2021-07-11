-- -------------------------------------------------
-- Add float rotations
-- -------------------------------------------------
ALTER TABLE `characters` 
ADD COLUMN `yaw` FLOAT NOT NULL DEFAULT '0' AFTER `z`,
ADD COLUMN `pitch` FLOAT NOT NULL DEFAULT '0' AFTER `yaw`,
ADD COLUMN `roll` FLOAT NOT NULL DEFAULT '0' AFTER `pitch`;

ALTER TABLE `housings` 
ADD COLUMN `yaw` FLOAT NOT NULL DEFAULT '0' AFTER `z`,
ADD COLUMN `pitch` FLOAT NOT NULL DEFAULT '0' AFTER `yaw`,
ADD COLUMN `roll` FLOAT NOT NULL DEFAULT '0' AFTER `pitch`;

ALTER TABLE `doodads` 
ADD COLUMN `yaw` FLOAT NOT NULL DEFAULT '0' AFTER `z`,
ADD COLUMN `pitch` FLOAT NOT NULL DEFAULT '0' AFTER `yaw`,
ADD COLUMN `roll` FLOAT NOT NULL DEFAULT '0' AFTER `pitch`;
