-- Update items table with new values
ALTER TABLE items
MODIFY COLUMN slot_type ENUM('Equipment','Inventory','Bank','Trade','Mail','System','EquipmentMate','EquipmentSlave') CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL COMMENT 'Internal Container Type';