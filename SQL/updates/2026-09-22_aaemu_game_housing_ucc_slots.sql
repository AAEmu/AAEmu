USE aaemu_game;

-- Applied user-content (UCC) slots of player buildings.
-- Own table instead of columns on the shared housings row; the server degrades to
-- memory-only slots when this table is missing.
CREATE TABLE IF NOT EXISTS `housing_ucc_slots` (
  `house_id` int unsigned NOT NULL,
  `slot` tinyint unsigned NOT NULL,
  `ucc_id` bigint unsigned NOT NULL DEFAULT '0',
  `ucc_kind` int unsigned NOT NULL DEFAULT '0',
  `ucc_position` int unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`house_id`,`slot`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Applied user-content slots of player buildings';
