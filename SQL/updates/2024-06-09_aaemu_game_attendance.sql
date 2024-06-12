-- ----------------------------
-- Table structure for attendances
-- ----------------------------
DROP TABLE IF EXISTS `attendances`;
CREATE TABLE `attendances`  (
  `id` tinyint unsigned NOT NULL,
  `owner` BIGINT UNSIGNED NOT NULL,
  `account_attendance` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `accept` tinyint(1) NOT NULL,
  PRIMARY KEY (`id`,`owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci;