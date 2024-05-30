SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

-- ----------------------------
-- Table structure for residents
-- ----------------------------
DROP TABLE IF EXISTS `residents`;
CREATE TABLE `residents`  (
  `id` int NOT NULL AUTO_INCREMENT,
  `zone_group_id` int NOT NULL,
  `point` int NULL DEFAULT NULL,
  `resident_token` int NULL DEFAULT NULL,
  `development_stage` tinyint(1) NOT NULL,
  `zone_point` int NULL DEFAULT NULL,
  `charge` datetime NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for resident_members
-- ----------------------------
DROP TABLE IF EXISTS `resident_members`;
CREATE TABLE `resident_members`  (
  `id` int NOT NULL,
  `resident_id` int NOT NULL,
  `name` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `level` tinyint(1) NOT NULL,
  `family` int NULL DEFAULT NULL,
  `service_point` int NULL DEFAULT NULL,
  PRIMARY KEY (`id`, `resident_id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = Dynamic;

SET FOREIGN_KEY_CHECKS = 1;
