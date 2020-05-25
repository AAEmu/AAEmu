/*
 Navicat Premium Data Transfer

 Source Server         : aaemu
 Source Server Type    : MySQL
 Source Server Version : 80015
 Source Host           : localhost:3306
 Source Schema         : aaemu_game

 Target Server Type    : MySQL
 Target Server Version : 80015
 File Encoding         : 65001

 Date: 18/02/2019 11:31:10
*/

SET NAMES utf8;
SET FOREIGN_KEY_CHECKS = 0;

-- ----------------------------
-- Table structure for friends
-- ----------------------------
DROP TABLE IF EXISTS `friends`;
CREATE TABLE `friends`  (
  `id` int(11) NOT NULL,
  `friend_id` int(11) NOT NULL,
  `owner` int(11) NOT NULL,
  PRIMARY KEY (`id`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for portal_book_coords
-- ----------------------------
DROP TABLE IF EXISTS `portal_book_coords`;
CREATE TABLE `portal_book_coords`  (
  `id` int(11) NOT NULL,
  `name` varchar(128) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `x` int(11) NULL DEFAULT 0,
  `y` int(11) NULL DEFAULT 0,
  `z` int(11) NULL DEFAULT 0,
  `zone_id` int(11) NULL DEFAULT 0,
  `z_rot` int(11) NULL DEFAULT 0,
  `sub_zone_id` int(11) NULL DEFAULT 0,
  `owner` int(11) NOT NULL,
  PRIMARY KEY (`id`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for portal_visited_district
-- ----------------------------
DROP TABLE IF EXISTS `portal_visited_district`;
CREATE TABLE `portal_visited_district`  (
  `id` int(11) NOT NULL,
  `subzone` int(11) NOT NULL,
  `owner` int(11) NOT NULL,
  PRIMARY KEY (`id`, `subzone`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci ROW_FORMAT = Dynamic;

SET FOREIGN_KEY_CHECKS = 1;
