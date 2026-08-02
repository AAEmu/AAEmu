USE aaemu_game;

CREATE TABLE IF NOT EXISTS `character_skill_active_types` (
  `owner` int unsigned NOT NULL COMMENT 'Character id',
  `heir_skill_type` int unsigned NOT NULL COMMENT 'Client Heir-skill category key',
  `skill_type` int unsigned NOT NULL COMMENT 'Client skill entry key',
  `active_type` tinyint unsigned NOT NULL COMMENT 'SkillActiveType value',
  PRIMARY KEY (`owner`,`heir_skill_type`,`skill_type`) USING BTREE,
  KEY `idx_character_skill_active_types_owner` (`owner`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Per-character skill visibility and activation state';

CREATE TABLE IF NOT EXISTS `heir_skill_activations` (
  `owner` int unsigned NOT NULL COMMENT 'Character id',
  `heir_skill_id` int unsigned NOT NULL COMMENT 'Selected heir_skills content row',
  `successor_skill_id` int unsigned NOT NULL COMMENT 'Selected heir_skill_details skill_id',
  PRIMARY KEY (`owner`,`heir_skill_id`) USING BTREE,
  KEY `idx_heir_skill_activations_owner` (`owner`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Active Heir-skill successor selections';
