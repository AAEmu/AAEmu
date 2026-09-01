USE aaemu_game;

-- Skillsaver (ability set) slots. usable count + daily free activations live on characters;
-- each saved triad and its learned skills/passives live in the child tables.

ALTER TABLE `characters`
  ADD COLUMN `usable_abil_set_slot_count` tinyint unsigned NOT NULL DEFAULT 1
    COMMENT 'How many skillsaver slots the character may use (expanded over time)' AFTER `expanded_expert`,
  ADD COLUMN `used_free_abil_set_activation` tinyint unsigned NOT NULL DEFAULT 0
    COMMENT 'Free skillsaver activations consumed this reset window' AFTER `usable_abil_set_slot_count`;

CREATE TABLE IF NOT EXISTS `ability_sets` (
  `owner` int unsigned NOT NULL,
  `slot` tinyint unsigned NOT NULL,
  `ability1` tinyint unsigned NOT NULL DEFAULT 30,
  `ability2` tinyint unsigned NOT NULL DEFAULT 30,
  `ability3` tinyint unsigned NOT NULL DEFAULT 30,
  PRIMARY KEY (`owner`, `slot`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Saved skillsaver ability triads';

CREATE TABLE IF NOT EXISTS `ability_set_skills` (
  `owner` int unsigned NOT NULL,
  `slot` tinyint unsigned NOT NULL,
  `skill_id` int unsigned NOT NULL,
  `is_passive` tinyint(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`owner`, `slot`, `skill_id`, `is_passive`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Skills/passives snapshotted into a skillsaver slot';
