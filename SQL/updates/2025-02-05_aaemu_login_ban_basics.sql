ALTER TABLE `users`
	ADD COLUMN `banned` INT UNSIGNED NOT NULL DEFAULT 0 AFTER `updated_at`,
	ADD COLUMN `ban_reason` INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Ban reason to report back' AFTER `banned`;