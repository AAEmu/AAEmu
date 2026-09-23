-- GF-W13 saga-group progression: per-character chronicle (saga book) group records and the
-- idempotent reward-grant ledger. Additive.

CREATE TABLE IF NOT EXISTS `character_saga_groups` (
  `owner` int unsigned NOT NULL COMMENT 'characters.id',
  `saga_quest_group_id` int unsigned NOT NULL COMMENT 'saga_quest_groups.id',
  `status` tinyint NOT NULL DEFAULT 0 COMMENT 'SagaGroupStatus: 0 active, 1 complete; row absent = locked',
  `completed_count` smallint unsigned NOT NULL DEFAULT 0 COMMENT 'Completed member quests of the group',
  PRIMARY KEY (`owner`,`saga_quest_group_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Saga group progression per character';

CREATE TABLE IF NOT EXISTS `character_saga_reward_grants` (
  `owner` int unsigned NOT NULL COMMENT 'characters.id',
  `saga_quest_group_id` int unsigned NOT NULL COMMENT 'saga_quest_groups.id',
  `grant_key` int unsigned NOT NULL COMMENT 'Content key of the grant (group milestone_id; GF-W14 milestone keys)',
  PRIMARY KEY (`owner`,`saga_quest_group_id`,`grant_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Saga rewards granted exactly once per character';
