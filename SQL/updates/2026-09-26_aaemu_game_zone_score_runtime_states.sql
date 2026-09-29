USE aaemu_game;

-- Live zone-score state for one zone group.
--
-- zone_score_kinds.db_save decides which kinds get a row here: a kind content marks transient keeps its
-- score in memory for the session and is never written, so it always restarts from the shipped level-zero
-- baseline. The score is the authority; `level` is written next to it for inspection but is re-derived from
-- the score through zone_score_levels on load, so a stale or hand-edited level cannot survive a restart.
--
-- max_score clamping is a zone_score_kinds column, not a schema constraint: the rules refuse to apply a
-- delta past the cap and the row stores the clamped value, which is why score is BIGINT here while the
-- catalog column is an int.
CREATE TABLE IF NOT EXISTS `zone_score_runtime_states` (
  `zone_group_id` INT UNSIGNED NOT NULL COMMENT 'zone_score_contents.zone_group_id that owns the kind',
  `zone_score_kind_id` INT UNSIGNED NOT NULL COMMENT 'zone_score_kinds.id',
  `score` BIGINT NOT NULL DEFAULT '0',
  `level` INT NOT NULL DEFAULT '0' COMMENT 'denormalized copy of the level the score resolves to',
  PRIMARY KEY (`zone_group_id`, `zone_score_kind_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Durable zone-score values and levels, capped by zone_score_kinds.max_score';
