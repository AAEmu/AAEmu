-- Preserve the discriminator required to decode each persisted item detail body.
ALTER TABLE `items`
ADD COLUMN `detail_type` TINYINT UNSIGNED NOT NULL DEFAULT '0' AFTER `count`;
