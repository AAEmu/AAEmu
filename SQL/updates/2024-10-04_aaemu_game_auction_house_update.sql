-- ----------------------------------
-- Table structure for auction_house
-- ----------------------------------
DROP TABLE IF EXISTS `auction_house`;
CREATE TABLE `auction_house`  (
  `id` bigint NOT NULL AUTO_INCREMENT,
  `duration` tinyint NOT NULL,
  `item_id` int NOT NULL,
  `object_id` bigint NOT NULL,
  `grade` tinyint(1) NOT NULL,
  `flags` tinyint(1) NOT NULL,
  `stack_size` int NOT NULL,
  `detail_type` tinyint(1) NOT NULL,
  `details` blob NULL,
  `creation_time` datetime NOT NULL,
  `end_time` datetime NOT NULL,
  `lifespan_mins` int NOT NULL,
  `made_unit_id` int NOT NULL,
  `world_id` tinyint NOT NULL,
  `unsecure_date_time` datetime NOT NULL,
  `unpack_date_time` datetime NOT NULL,
  `charge_use_skill_time` datetime NOT NULL,
  `world_id_2` tinyint NOT NULL,
  `client_id` int NOT NULL,
  `client_name` varchar(45) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `start_money` int NOT NULL,
  `direct_money` int NOT NULL,
  `charge_percent` int NOT NULL,
  `bid_world_id` tinyint(1) NOT NULL,
  `bidder_id` int NOT NULL,
  `bidder_name` varchar(45) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `bid_money` int NOT NULL,
  `extra` int NOT NULL,
  `min_stack` int NOT NULL,
  `max_stack` int NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci COMMENT = 'Listed AH Items' ROW_FORMAT = DYNAMIC;

SET FOREIGN_KEY_CHECKS = 1;
