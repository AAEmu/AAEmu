-- 1. Add a temporary field old_slot_type
ALTER TABLE `item_containers` 
ADD COLUMN `old_slot_type` ENUM('Equipment', 'Inventory', 'Bank', 'Trade', 'Mail', 'System', 'EquipmentMate') CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci 
DEFAULT NULL COMMENT 'Old slot type for conversion';

-- 2. Copy values ​​from slot_type to old_slot_type
UPDATE `item_containers` 
SET `old_slot_type` = `slot_type`;

-- 3. Delete the old slot_type
ALTER TABLE `item_containers` 
DROP COLUMN `slot_type`;

-- 4. Add a new slot_type with type int
ALTER TABLE `item_containers` 
ADD COLUMN `slot_type` INT NOT NULL DEFAULT 0 COMMENT 'Internal Container Type';

-- 5. Update slot_type based on old_slot_type
UPDATE `item_containers` 
SET `slot_type` = CASE 
    WHEN `old_slot_type` = 'Equipment' THEN 1
    WHEN `old_slot_type` = 'Inventory' THEN 2
    WHEN `old_slot_type` = 'Bank' THEN 3
    WHEN `old_slot_type` = 'Trade' THEN 4
    WHEN `old_slot_type` = 'Mail' THEN 5
    WHEN `old_slot_type` = 'System' THEN 255
    WHEN `old_slot_type` = 'EquipmentMate' THEN 252
    ELSE 0  -- Set to 0 for all other values
END;

-- 6. Delete the temporary field old_slot_type if it is no longer needed
ALTER TABLE `item_containers` 
DROP COLUMN `old_slot_type`;
