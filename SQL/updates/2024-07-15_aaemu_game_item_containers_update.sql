-- Update item_containers table with new values
ALTER TABLE item_containers
MODIFY COLUMN slot_type ENUM('Equipment','Inventory','Bank','Trade','Mail','System','EquipmentMate','EquipmentSlave') CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL COMMENT 'Internal Container Type';