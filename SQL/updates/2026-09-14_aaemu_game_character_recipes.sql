USE aaemu_game;

CREATE TABLE IF NOT EXISTS `character_recipes` (
  `owner` int unsigned NOT NULL COMMENT 'Character id',
  `craft_id` int unsigned NOT NULL COMMENT 'Craft id from game content, learned from the item_recipes entry of a recipe item',
  `learned_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'When the recipe item was used',
  PRIMARY KEY (`owner`,`craft_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Recipes a character has learned by using a recipe item';
