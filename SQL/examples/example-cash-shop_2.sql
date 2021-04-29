/*
 Navicat Premium Data Transfer

 Source Server         : localhost
 Source Server Type    : MySQL
 Source Server Version : 80019
 Source Host           : 127.0.0.1:3306
 Source Schema         : aaemu_game

 Target Server Type    : MySQL
 Target Server Version : 80019
 File Encoding         : 65001

 Date: 01/04/2021 22:39:41
*/

SET NAMES utf8;
SET FOREIGN_KEY_CHECKS = 0;

-- ----------------------------
-- Table structure for cash_shop_item
-- ----------------------------
DROP TABLE IF EXISTS `cash_shop_item`;
CREATE TABLE `cash_shop_item`  (
  `id` int UNSIGNED NOT NULL AUTO_INCREMENT COMMENT 'shop_id',
  `uniq_id` int UNSIGNED NULL DEFAULT 0 COMMENT '唯一ID',
  `cash_name` varchar(255) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL COMMENT '出售名称',
  `main_tab` tinyint UNSIGNED NULL DEFAULT 1 COMMENT '主分类1-6',
  `sub_tab` tinyint UNSIGNED NULL DEFAULT 1 COMMENT '子分类1-7',
  `level_min` tinyint UNSIGNED NULL DEFAULT 0 COMMENT '等级限制',
  `level_max` tinyint UNSIGNED NULL DEFAULT 0 COMMENT '等级限制',
  `item_template_id` int UNSIGNED NULL DEFAULT 0 COMMENT '物品模板id',
  `is_sell` tinyint UNSIGNED NULL DEFAULT 0 COMMENT '是否出售',
  `is_hidden` tinyint UNSIGNED NULL DEFAULT 0 COMMENT '是否隐藏',
  `limit_type` tinyint UNSIGNED NULL DEFAULT 0,
  `buy_count` smallint UNSIGNED NULL DEFAULT 0,
  `buy_type` tinyint UNSIGNED NULL DEFAULT 0,
  `buy_id` int UNSIGNED NULL DEFAULT 0,
  `start_date` datetime NULL DEFAULT '0001-01-01 00:00:00' COMMENT '出售开始',
  `end_date` datetime NULL DEFAULT '0001-01-01 00:00:00' COMMENT '出售截止',
  `type` tinyint UNSIGNED NULL DEFAULT 0 COMMENT '货币类型',
  `price` int UNSIGNED NULL DEFAULT 0 COMMENT '价格',
  `remain` int UNSIGNED NULL DEFAULT 0 COMMENT '剩余数量',
  `bonus_type` int UNSIGNED NULL DEFAULT 0 COMMENT '赠送类型',
  `bouns_count` int UNSIGNED NULL DEFAULT 0 COMMENT '赠送数量',
  `cmd_ui` tinyint UNSIGNED NULL DEFAULT 0 COMMENT '是否限制一人一次',
  `item_count` int UNSIGNED NULL DEFAULT 1 COMMENT '捆绑数量',
  `select_type` tinyint UNSIGNED NULL DEFAULT 0,
  `default_flag` tinyint UNSIGNED NULL DEFAULT 0,
  `event_type` tinyint UNSIGNED NULL DEFAULT 0 COMMENT '活动类型',
  `event_date` datetime NULL DEFAULT '0001-01-01 00:00:00' COMMENT '活动时间',
  `dis_price` int UNSIGNED NULL DEFAULT 0 COMMENT '当前售价',
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 60100153 CHARACTER SET = utf8 COLLATE = utf8_general_ci COMMENT = '此表来自于代码中的字段并去除重复字段生成。字段名称和内容以代码为准。' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of cash_shop_item
-- ----------------------------
INSERT INTO `cash_shop_item` VALUES (10100004, 10100004, 'Expansion Scroll', 1, 1, 0, 0, 8000025, 1, 1, 99, 1, 1, 0, '2019-05-01 14:10:08', '2055-06-16 14:10:12', 0, 5, 88, 0, 0, 0, 1, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (10100005, 10100005, 'Hall of Fame: Rank 1', 1, 1, 0, 0, 30538, 0, 1, 99, 1, 1, 0, '2019-05-01 14:10:08', '2055-06-16 14:10:12', 0, 5, 97, 0, 0, 0, 1, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (10100007, 10100007, 'Superior Red Regrade Charm', 1, 1, 0, 0, 31977, 0, 1, 99, 1, 1, 0, '2019-05-01 14:10:08', '2055-06-16 14:10:12', 0, 5, 83, 0, 0, 0, 1, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (10100008, 10100008, 'Spooky Chest', 1, 1, 0, 0, 30559, 0, 1, 99, 1, 1, 0, '2019-05-01 14:10:08', '2055-06-16 14:10:12', 0, 5, 96, 0, 0, 0, 1, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (10100009, 10100009, 'Glider : Sloth', 1, 1, 0, 0, 30621, 0, 1, 30, 1, 1, 0, '2019-05-01 14:10:08', '2055-06-16 14:10:12', 0, 5, 95, 0, 0, 0, 1, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (10100010, 10100010, 'Crimson Lightning', 1, 1, 0, 0, 32566, 0, 1, 30, 1, 1, 0, '2019-05-01 14:10:08', '2055-06-16 14:10:12', 0, 5, 84, 0, 0, 0, 0, 1, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (10100011, 10100011, 'Adventurer\'s Pouch', 1, 1, 0, 0, 34092, 0, 1, 50, 1, 1, 0, '2019-05-01 14:10:08', '2055-06-16 14:10:12', 0, 5, 85, 0, 0, 0, 1, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (10100012, 10100012, 'Refined Hereafter Stone', 1, 2, 0, 0, 19972, 0, 1, 99, 1, 1, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (10100113, 10100013, 'Auramancy Disciple', 1, 3, 0, 0, 33164, 0, 1, 99, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (10100114, 10100314, 'Loyalty Token 500-Pack', 1, 4, 0, 0, 28794, 0, 1, 99, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (10100115, 10100015, 'Tempering Flux: Weapons', 1, 5, 0, 0, 32946, 0, 1, 99, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (10100116, 10100016, 'Champion\'s Might', 1, 6, 0, 0, 29181, 0, 1, 99, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (10100117, 10100017, '1-7', 1, 7, 0, 0, 29182, 0, 1, 99, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (20100004, 10100004, 'Elating Dawn', 1, 1, 0, 0, 31145, 0, 1, 30, 1, 0, 0, '2019-05-01 14:10:08', '2055-06-16 14:10:12', 0, 5, 90, 0, 0, 0, 1, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (20100118, 20100018, 'Scroll: Red Bull', 2, 1, 0, 0, 28773, 0, 1, 30, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (20100119, 20100019, '2-1', 2, 1, 0, 0, 31121, 0, 1, 50, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (20100120, 20100020, '2-2', 2, 2, 0, 0, 31122, 0, 1, 99, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (20100121, 20100021, '2-3', 2, 3, 0, 0, 31123, 0, 1, 99, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (20100122, 20100022, '2-4', 2, 4, 0, 0, 31124, 0, 1, 99, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (20100123, 20100023, '2-5', 2, 5, 0, 0, 31125, 0, 1, 99, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (20100124, 20100024, '2-6', 2, 6, 0, 0, 31126, 0, 1, 99, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (20100125, 20100025, '2-7', 2, 7, 0, 0, 31127, 0, 1, 99, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (30100126, 20100026, 'Veroe Staff', 3, 1, 0, 0, 31128, 0, 1, 30, 1, 1, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (30100127, 20100027, 'Legendary Dragon Wings', 3, 2, 0, 0, 29039, 0, 1, 30, 1, 1, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (30100128, 20100028, 'Earthen Roar', 3, 3, 0, 0, 14879, 0, 1, 50, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (30100129, 20100029, 'Сосное семя', 3, 4, 0, 0, 34312, 0, 1, 99, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (30100130, 20100030, 'Gazebo Farm Design', 3, 5, 0, 0, 34438, 0, 1, 99, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (30100131, 20100031, 'Assassin\'s Courage', 3, 6, 0, 0, 29182, 0, 1, 99, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (30100132, 20100032, '3-7', 3, 7, 0, 0, 29197, 0, 1, 99, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (40100133, 20100033, '4-1', 4, 1, 0, 0, 29198, 0, 1, 99, 0, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (40100134, 20100034, 'Wrapped Tax Certificate 10-pack', 4, 2, 0, 0, 34825, 1, 1, 99, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (40100135, 20100035, 'Rowan Alexander Portrait', 4, 3, 0, 0, 33022, 1, 1, 99, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (40100136, 20100036, '4-4', 4, 4, 0, 0, 29201, 0, 1, 30, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (40100137, 20100037, '4-6', 4, 5, 0, 0, 29202, 0, 1, 30, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (40100138, 20100038, '4-6', 4, 6, 0, 0, 29203, 0, 1, 50, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (40100139, 20100039, '4-7', 4, 7, 0, 0, 29204, 0, 1, 99, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (50100140, 20100040, 'Conjurer\'s Wisdom', 5, 1, 0, 0, 29200, 0, 1, 99, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (50100141, 20100041, '5-2', 5, 2, 0, 0, 29206, 0, 1, 99, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (50100142, 20100042, '5-3', 5, 3, 0, 0, 29207, 0, 1, 99, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (50100143, 20100043, '5-4', 5, 4, 0, 0, 29208, 0, 1, 99, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (50100144, 20100044, 'Devilish Temptation', 5, 5, 0, 0, 34277, 0, 1, 30, 1, 1, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (50100145, 20100045, 'Angelic Whisper', 5, 6, 0, 0, 34276, 0, 1, 30, 1, 1, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (50100146, 20100046, 'Fleetpaw Bjorne', 5, 7, 0, 0, 28420, 0, 1, 50, 1, 1, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (60100147, 20100047, '6-1', 6, 1, 0, 0, 29212, 0, 1, 99, 1, 1, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (60100148, 20100048, 'Denim Mecha Coveralls', 6, 2, 0, 0, 29909, 0, 1, 99, 1, 1, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (60100149, 20100049, 'Superior Yellow Regrade Charm', 6, 3, 0, 0, 31975, 0, 1, 99, 1, 1, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (60100150, 20100050, 'Scroll: Redwood Roadster', 6, 4, 0, 0, 29870, 0, 1, 0, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (60100151, 20100051, 'Captive Tarian the Grim', 6, 5, 0, 0, 27209, 0, 1, 0, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (60100152, 20100052, 'Captive Blackscale Corps Deceiver', 6, 6, 0, 0, 27211, 0, 1, 0, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);
INSERT INTO `cash_shop_item` VALUES (60100153, 20100053, 'Gaffrion\'s Ring', 6, 7, 0, 0, 27394, 0, 1, 0, 1, 0, 0, '0001-01-01 00:00:00', '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', 0);

SET FOREIGN_KEY_CHECKS = 1;
