ALTER TABLE `aaemu_game`.`items` 
CHANGE COLUMN `slot_type` `slot_type` ENUM('Equipment', 'Inventory', 'Bank', 'Trade', 'Mail', 'System') NOT NULL ;
