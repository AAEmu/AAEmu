-- -------------------------------------------------
-- Add mate support to item containers
-- -------------------------------------------------
ALTER TABLE `item_containers` ADD `mate_id` INT UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Owning Mate Id' AFTER `owner_id`;
ALTER TABLE `item_containers` CHANGE `slot_type` `slot_type` ENUM('Equipment','Inventory','Bank','Trade','Mail','System','EquipmentMate') CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT 'Internal Container Type';
ALTER TABLE `items` CHANGE `slot_type` `slot_type` ENUM('Equipment','Inventory','Bank','Trade','Mail','System','EquipmentMate') CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL;