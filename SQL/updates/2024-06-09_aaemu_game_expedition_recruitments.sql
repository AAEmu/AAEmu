-- ----------------------------
-- Table structure for expedition_recruitments
-- ----------------------------
DROP TABLE IF EXISTS `expedition_recruitments`;
CREATE TABLE `expedition_recruitments`  (
  `expedition_id` int NOT NULL,
  `name` varchar(255) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `level` int NULL DEFAULT NULL,
  `owner_name` varchar(255) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `introduce` varchar(255) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `reg_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `end_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `member_count` int NULL DEFAULT NULL,
  `interest` int NULL DEFAULT NULL,
  `apply` tinyint(1) NOT NULL,
  PRIMARY KEY (`expedition_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci COMMENT = 'Guild recruitments';