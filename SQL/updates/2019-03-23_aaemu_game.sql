ALTER TABLE `housings` ADD `name` VARCHAR(128) NOT NULL AFTER `template_id`;
ALTER TABLE `housings` ADD `co_owner` INT UNSIGNED NOT NULL AFTER `owner`;