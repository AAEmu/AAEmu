-- -------------------------------------------------
-- Updated In Game Cash shop tables
-- -------------------------------------------------
ALTER TABLE `ics_skus` ADD COLUMN `pay_item_type` INT(10) UNSIGNED NOT NULL DEFAULT '0' AFTER `bonus_item_count`;
