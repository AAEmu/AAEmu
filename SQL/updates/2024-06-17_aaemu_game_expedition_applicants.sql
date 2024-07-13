-- ----------------------------
-- Table structure for expedition_applicants
-- ----------------------------
DROP TABLE IF EXISTS `expedition_applicants`;
CREATE TABLE `expedition_applicants`  (
  `expedition_id` int NOT NULL,
  `character_id` int NOT NULL,
  `character_name` varchar(255) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `character_level` tinyint(1) NOT NULL,
  `memo` varchar(255) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `reg_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  PRIMARY KEY (`expedition_id`, `character_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci COMMENT = 'Guild applicants';