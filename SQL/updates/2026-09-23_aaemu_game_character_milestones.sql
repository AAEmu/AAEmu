-- GF-W14 milestone progression: per-character milestone records. The grant-once rows ride
-- GF-W13's character_saga_reward_grants (saga_quest_group_id = the non-group milestone scope,
-- grant_key = milestones.id). Additive.

CREATE TABLE IF NOT EXISTS `character_milestones` (
  `owner` int unsigned NOT NULL COMMENT 'characters.id',
  `milestone_id` int unsigned NOT NULL COMMENT 'milestones.id',
  `status` tinyint NOT NULL DEFAULT 0 COMMENT 'MilestoneStatus: 0 active, 1 complete; row absent = not started',
  `completed_count` smallint unsigned NOT NULL DEFAULT 0 COMMENT 'Finished quests of the milestone trigger chain',
  PRIMARY KEY (`owner`,`milestone_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Milestone progression per character';
