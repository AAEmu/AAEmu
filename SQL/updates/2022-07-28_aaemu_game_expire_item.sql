-- -------------------------------------------------
-- Item Expire Times
-- -------------------------------------------------
ALTER TABLE `items` 
ADD `expire_time` DATETIME NULL DEFAULT NULL COMMENT 'Fixed time expire' AFTER `ucc`, 
ADD `expire_online_minutes` DOUBLE NOT NULL DEFAULT '0' COMMENT 'Time left when player online' AFTER `expire_time`,
ADD `charge_time` DATETIME NULL DEFAULT NULL COMMENT 'Time charged items got activated' AFTER `expire_online_minutes`, 
ADD `charge_count` INT NOT NULL DEFAULT '0' COMMENT 'Number of charges left' AFTER `charge_time`;
