-- -----------------------------------------------------------------------------
-- Change name of some fields of the crime table, and add a complete time field
-- -----------------------------------------------------------------------------

ALTER TABLE `crime`
	CHANGE COLUMN `arg1` `skill_id` INT UNSIGNED NULL DEFAULT NULL COMMENT 'Used Skill to report' AFTER `report_time`,
	CHANGE COLUMN `arg2` `next_func_group` INT UNSIGNED NULL DEFAULT NULL COMMENT 'Next FuncGroup of the evidence' AFTER `skill_id`,
	CHANGE COLUMN `arg3` `func_id` INT UNSIGNED NULL DEFAULT NULL COMMENT 'Current DoodadFunc Id' AFTER `next_func_group`;
	
ALTER TABLE `crime`
	ADD COLUMN `judgement_time` DATETIME NULL DEFAULT NULL COMMENT 'Time when this event has been processed' AFTER `msg`;