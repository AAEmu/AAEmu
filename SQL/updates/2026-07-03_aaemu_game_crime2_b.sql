-- ----------------------------------------
-- Additional counters for Character table
-- ----------------------------------------

ALTER TABLE `characters`
	ADD COLUMN `arrest_count` INT NULL DEFAULT '0' AFTER `online_time`,
	ADD COLUMN `accept_guilty_count` INT NULL DEFAULT '0' AFTER `arrest_count`,
	ADD COLUMN `accept_trial_count` INT NULL DEFAULT '0' AFTER `accept_guilty_count`,
	ADD COLUMN `not_guilty_count` INT NULL DEFAULT '0' AFTER `accept_trial_count`,
	ADD COLUMN `guilty_count` INT NULL DEFAULT '0' AFTER `not_guilty_count`,
	ADD COLUMN `evidence_reported_count` INT NULL DEFAULT '0' AFTER `guilty_count`,
	ADD COLUMN `bot_reported_count` INT NULL DEFAULT '0' AFTER `evidence_reported_count`,
	ADD COLUMN `reported_as_bot_count` INT NULL DEFAULT '0' AFTER `bot_reported_count`,
	ADD COLUMN `offline_guilty_time` INT NULL DEFAULT '0' COMMENT 'Guilty time for skipped trials' AFTER `evidence_reported_count`,
	ADD COLUMN `offline_guilty_region` INT NULL DEFAULT '0' COMMENT 'Region for skipped trials' AFTER `offline_guilty_time`;