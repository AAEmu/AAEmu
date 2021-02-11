ALTER TABLE `characters` 
ADD COLUMN `return_district_id` int NOT NULL DEFAULT 0 AFTER `deleted`;
