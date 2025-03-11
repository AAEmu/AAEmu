-- ----------------------------------
-- Table structure for auction_house
-- ----------------------------------
DROP TABLE IF EXISTS `auction_house`;
CREATE TABLE `auction_house`  (
  `id` bigint NOT NULL AUTO_INCREMENT,
  `duration` tinyint NOT NULL,
  `item_id` bigint NOT NULL,
  `end_time` datetime NOT NULL,
  `world_id` tinyint NOT NULL,
  `client_id` int NOT NULL,
  `client_name` varchar(45) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `start_money` int NOT NULL,
  `direct_money` int NOT NULL,
  `charge_percent` int NOT NULL,
  `bid_world_id` int NOT NULL,
  `bidder_id` int NOT NULL,
  `bidder_name` varchar(45) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `bid_money` int NOT NULL,
  `extra` int NOT NULL,
  `min_stack` int NOT NULL,
  `max_stack` int NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci COMMENT = 'Listed AH Items' ROW_FORMAT = DYNAMIC;

SET FOREIGN_KEY_CHECKS = 1;
