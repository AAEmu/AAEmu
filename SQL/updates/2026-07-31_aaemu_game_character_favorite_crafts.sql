USE aaemu_game;

CREATE TABLE IF NOT EXISTS `character_favorite_crafts` (
  `owner` int unsigned NOT NULL COMMENT 'Character id',
  `craft_type` int unsigned NOT NULL COMMENT 'Craft recipe id from game content',
  PRIMARY KEY (`owner`,`craft_type`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Per-character favorite crafting recipes';
