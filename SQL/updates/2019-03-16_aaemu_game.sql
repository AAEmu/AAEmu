DROP TABLE IF EXISTS `expedition_members`;
CREATE TABLE IF NOT EXISTS `expedition_members` (
  `character_id` int(11) NOT NULL,
  `expedition_id` int(11) NOT NULL,
  `name` varchar(128) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `level` tinyint(4) UNSIGNED NOT NULL,
  `role` tinyint(4) UNSIGNED NOT NULL,
  `last_leave_time` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `ability1` tinyint(4) UNSIGNED NOT NULL,
  `ability2` tinyint(4) UNSIGNED NOT NULL,
  `ability3` tinyint(4) UNSIGNED NOT NULL,
  `memo` varchar(128) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  PRIMARY KEY (`character_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;

DROP TABLE IF EXISTS `expedition_role_policies`;
CREATE TABLE IF NOT EXISTS `expedition_role_policies` (
  `expedition_id` int(11) NOT NULL,
  `role` tinyint(4) UNSIGNED NOT NULL,
  `name` varchar(128) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `dominion_declare` tinyint(1) NOT NULL,
  `invite` tinyint(1) NOT NULL,
  `expel` tinyint(1) NOT NULL,
  `promote` tinyint(1) NOT NULL,
  `dismiss` tinyint(1) NOT NULL,
  `chat` tinyint(1) NOT NULL,
  `manager_chat` tinyint(1) NOT NULL,
  `siege_master` tinyint(1) NOT NULL,
  `join_siege` tinyint(1) NOT NULL,
  PRIMARY KEY (`expedition_id`,`role`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;