-- ----------------------------
-- items update
-- ----------------------------
ALTER TABLE `items` 
CHANGE COLUMN `slot_type` `slot_type` ENUM('Equipment', 'Inventory', 'Bank', 'Trade', 'Mail') NOT NULL ;
