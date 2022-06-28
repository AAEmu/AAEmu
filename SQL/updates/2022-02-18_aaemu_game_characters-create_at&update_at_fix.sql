-- -------------------------------------------------
-- CHANGE characters fields
-- -------------------------------------------------
ALTER TABLE `characters` MODIFY COLUMN `created_at` datetime(0) NOT NULL DEFAULT '0001-01-01 00:00:00' AFTER `slots`;
ALTER TABLE `characters` MODIFY COLUMN `updated_at` datetime(0) NOT NULL DEFAULT '0001-01-01 00:00:00' AFTER `created_at`;
