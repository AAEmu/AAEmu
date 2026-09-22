USE aaemu_game;

CREATE TABLE IF NOT EXISTS `conflict_zone_runtime_states` (
  `zone_group_id` SMALLINT UNSIGNED NOT NULL,
  `state` TINYINT UNSIGNED NOT NULL,
  `kill_count` INT UNSIGNED NOT NULL DEFAULT 0,
  `npc_kill_count` INT UNSIGNED NOT NULL DEFAULT 0,
  `quest_completion_count` INT UNSIGNED NOT NULL DEFAULT 0,
  `next_state_time` DATETIME(6) NULL,
  PRIMARY KEY (`zone_group_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Durable conflict-zone counters, state and transition deadline';
