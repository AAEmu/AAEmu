-- -------------------------------------------------
-- Add return_district to characters table
-- -------------------------------------------------
ALTER TABLE `characters` 
ADD COLUMN `return_district` int(11) NOT NULL DEFAULT 0 AFTER `deleted`;
