USE aaemu_game;

-- Hero diplomacy (CSFactionRelationRequest / Response). An accepted request overlays a neutral
-- relation on a pair of nations for content_configs faction_diplomacy_term minutes; the rows below
-- keep the live overlay, its history and the per-hero counters across a restart. Column order is the
-- client's relation entry (x2game-dev.dll 0x39398a90). Faction ids are stored ascending.

CREATE TABLE IF NOT EXISTS `faction_relations` (
  `faction1_id` INT UNSIGNED NOT NULL,
  `faction2_id` INT UNSIGNED NOT NULL,
  `state` TINYINT UNSIGNED NOT NULL,
  `next_state` TINYINT UNSIGNED NOT NULL,
  `update_unix` BIGINT NOT NULL,
  `change_unix` BIGINT NOT NULL,
  `updater_id` INT UNSIGNED NOT NULL,
  `updater_name` VARCHAR(128) NOT NULL,
  `confirmer_id` INT UNSIGNED NOT NULL,
  `confirmer_name` VARCHAR(128) NOT NULL,
  PRIMARY KEY (`faction1_id`, `faction2_id`),
  KEY `idx_faction_relations_change` (`change_unix`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Live hero diplomacy agreements overlaid on system_faction_relations';

CREATE TABLE IF NOT EXISTS `faction_relation_histories` (
  `id` BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `faction1_id` INT UNSIGNED NOT NULL,
  `faction2_id` INT UNSIGNED NOT NULL,
  `state` TINYINT UNSIGNED NOT NULL,
  `next_state` TINYINT UNSIGNED NOT NULL,
  `update_unix` BIGINT NOT NULL,
  `change_unix` BIGINT NOT NULL,
  `updater_id` INT UNSIGNED NOT NULL,
  `updater_name` VARCHAR(128) NOT NULL,
  `confirmer_id` INT UNSIGNED NOT NULL,
  `confirmer_name` VARCHAR(128) NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Past hero diplomacy agreements (SCFactionRelationHistory)';

-- other_id 0: the character's agreements concluded today (reset per UTC day by the reader).
-- other_id N: how often this hero denied requester N (never reset).
CREATE TABLE IF NOT EXISTS `faction_relation_counts` (
  `character_id` INT UNSIGNED NOT NULL,
  `other_id` INT UNSIGNED NOT NULL,
  `count` INT UNSIGNED NOT NULL,
  `updated_unix` BIGINT NOT NULL,
  PRIMARY KEY (`character_id`, `other_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Hero diplomacy request and denial counters (SCFactionRelationCount)';
