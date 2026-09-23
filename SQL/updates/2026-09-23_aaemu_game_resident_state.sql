USE aaemu_game;

-- Resident point and charge settlement.
-- character_resident_state: one row per resident per zone group, written at every
-- settlement (CSAddResidentServicePoint / CSAddResidentCharge / quest resident acts).
CREATE TABLE IF NOT EXISTS `character_resident_state` (
  `owner` int unsigned NOT NULL,
  `zone_group_id` smallint unsigned NOT NULL,
  `service_point` int unsigned NOT NULL DEFAULT 0,
  `charge` bigint unsigned NOT NULL DEFAULT 0,
  `hunting_charge` bigint unsigned NOT NULL DEFAULT 0,
  `updated_at` datetime(6) NOT NULL,
  PRIMARY KEY (`owner`, `zone_group_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Resident service points and charges per zone group';
