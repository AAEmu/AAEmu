USE aaemu_game;

-- GF-W10 resident point/charge settlement and local-development state.
-- character_resident_state: one row per resident per zone group, written at every
-- settlement (CSAddResidentServicePoint / CSAddResidentCharge / quest resident acts).
-- local_development_state: the last development level and doodad/board phases applied
-- for a zone group, so a restart can see what the world was last told.
CREATE TABLE IF NOT EXISTS `character_resident_state` (
  `owner` int unsigned NOT NULL,
  `zone_group_id` smallint unsigned NOT NULL,
  `service_point` int unsigned NOT NULL DEFAULT 0,
  `charge` bigint unsigned NOT NULL DEFAULT 0,
  `updated_at` datetime(6) NOT NULL,
  PRIMARY KEY (`owner`, `zone_group_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Resident service points and charges per zone group';

CREATE TABLE IF NOT EXISTS `local_development_state` (
  `zone_group_id` smallint unsigned NOT NULL,
  `development_level` int unsigned NOT NULL DEFAULT 0,
  `doodad_phase` int unsigned NOT NULL DEFAULT 0,
  `board_phase` int unsigned NOT NULL DEFAULT 0,
  `updated_at` datetime(6) NOT NULL,
  PRIMARY KEY (`zone_group_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Last applied local-development level and doodad/board phases per zone group';
