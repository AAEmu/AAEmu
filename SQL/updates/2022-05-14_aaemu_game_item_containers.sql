-- -------------------------------------------------
-- Item Containers
-- -------------------------------------------------
CREATE TABLE `item_containers` (
  `container_id` int unsigned NOT NULL,
  `container_type` varchar(64) COLLATE 'utf8mb4_general_ci' NOT NULL DEFAULT 'ItemContainer' COMMENT 'Partial Container Class Name',
  `slot_type` enum('Equipment','Inventory','Bank','Trade','Mail','System') NOT NULL COMMENT 'Internal Container Type',
  `container_size` int NOT NULL DEFAULT '50' COMMENT 'Maximum Container Size',
  `owner_id` int unsigned NOT NULL COMMENT 'Owning Character Id',
  PRIMARY KEY (`container_id`) 
) COLLATE 'utf8mb4_general_ci';

ALTER TABLE `items`
ADD `container_id` int unsigned NOT NULL DEFAULT '0' AFTER `template_id`;