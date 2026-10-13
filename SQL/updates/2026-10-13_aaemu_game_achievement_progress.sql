-- Per-character achievement state for the 1.2 achievement runtime
-- (`systems::social::achievement_runtime`): the record values the engine has reported
-- (`CharacterRecords`) and how far the character has got with each achievement
-- (`CharacterAchievements`).
--
-- `character_records.value` and `character_achievements.completed_at` are BIGINT: the runtime's
-- records are signed (the content stores -1 as a "no target" sentinel and the store keeps the
-- highest value ever reported), and `completed_at` is Unix seconds in UTC, NULL while the
-- achievement is still in progress, the same unit `db::unix` / `db::from_unix` use elsewhere.
-- `character_achievements.reward_delivered` is the reward receipt: 1 once the completion's reward
-- has been handed over (or deliberately settled), so a relog cannot pay it a second time.
--
-- `Character.Save` writes these rows after its `REPLACE INTO characters` (which deletes and
-- re-inserts the row of an existing id), so the ON DELETE CASCADE cannot drop them mid-save; it
-- fires when the character row itself is deleted.
--

CREATE TABLE IF NOT EXISTS `character_records` (
  `owner`     int unsigned NOT NULL COMMENT 'Character whose record this is',
  `record_id` int unsigned NOT NULL COMMENT 'CharRecords.Id from game data',
  `value`     bigint NOT NULL DEFAULT 0 COMMENT 'Highest value the engine reported for this record',
  PRIMARY KEY (`owner`, `record_id`),
  CONSTRAINT `fk_character_records_owner` FOREIGN KEY (`owner`) REFERENCES `characters` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Per-character achievement record values';

CREATE TABLE IF NOT EXISTS `character_achievements` (
  `owner`            int unsigned NOT NULL COMMENT 'Character whose progress this is',
  `achievement_id`   int unsigned NOT NULL COMMENT 'Achievements.Id from game data',
  `amount`           int NOT NULL DEFAULT 0 COMMENT 'How far the character has got (packet amount)',
  `completed_at`     bigint DEFAULT NULL COMMENT 'Unix seconds UTC when it was earned; NULL while in progress',
  `reward_delivered` tinyint NOT NULL DEFAULT 0 COMMENT '1 = the completion reward has been delivered, 0 = pending',
  PRIMARY KEY (`owner`, `achievement_id`),
  CONSTRAINT `fk_character_achievements_owner` FOREIGN KEY (`owner`) REFERENCES `characters` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Per-character achievement progress';
