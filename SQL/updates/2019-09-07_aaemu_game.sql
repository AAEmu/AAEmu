ALTER TABLE `characters` 
ADD COLUMN `deleted` INT(11) NOT NULL DEFAULT 0 AFTER `updated_at`;
