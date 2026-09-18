USE aaemu_game;

-- Equip slot reinforcement progress: one row per character per slot, holding the level reached and the
-- experience banked towards the next level. The ladders and their prices are content
-- (equip_slot_reinforces and friends), not stored here.

CREATE TABLE IF NOT EXISTS `character_equip_slot_reinforces` (
  `owner` int unsigned NOT NULL,
  `slot_type_id` tinyint unsigned NOT NULL,
  `level` tinyint NOT NULL DEFAULT 0,
  `exp` int NOT NULL DEFAULT 0,
  PRIMARY KEY (`owner`, `slot_type_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Equip slot reinforcement level and exp';

-- The artifact effects a character's equip slots obtained. A tier (an equip_slot_reinforce_level_effects
-- row) hands out one of its equip_slot_reinforce_unit_modifiers rows, so a slot has at most one effect per
-- tier and the row it rolled is what the character's stats are given. `applied` is the window's radio:
-- the effect lands switched on, and picking "None" switches it off without losing the roll.

CREATE TABLE IF NOT EXISTS `character_equip_slot_reinforce_effects` (
  `owner` int unsigned NOT NULL,
  `slot_type_id` tinyint unsigned NOT NULL,
  `level_effect_id` int unsigned NOT NULL,
  `unit_modifier_id` int unsigned NOT NULL,
  `applied` tinyint(1) NOT NULL DEFAULT 1,
  PRIMARY KEY (`owner`, `slot_type_id`, `level_effect_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Artifact effect each equip slot rolled, one row per tier';
