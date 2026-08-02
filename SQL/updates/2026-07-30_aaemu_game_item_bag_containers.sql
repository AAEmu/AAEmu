-- Persist nested coffer bags against the concrete item instance that owns them.
ALTER TABLE `item_containers`
ADD COLUMN `parent_item_id` BIGINT UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Owning ItemBag instance Id' AFTER `mate_id`;

CREATE INDEX `idx_item_containers_parent_item_id` ON `item_containers` (`parent_item_id`);
