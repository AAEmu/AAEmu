USE aaemu_game;

-- Equip slot reinforcement progress: one row per character per slot, holding the level reached, the
-- experience banked towards the next level and the level effect the slot runs. The ladders and their
-- prices are content (equip_slot_reinforces and friends), not stored here.

CREATE TABLE IF NOT EXISTS `character_equip_slot_reinforces` (
  `owner` int unsigned NOT NULL,
  `slot_type_id` tinyint unsigned NOT NULL,
  `level` tinyint NOT NULL DEFAULT 0,
  `exp` int NOT NULL DEFAULT 0,
  `level_effect_index` int NOT NULL DEFAULT -1,
  PRIMARY KEY (`owner`, `slot_type_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Equip slot reinforcement level, exp and chosen level effect';
