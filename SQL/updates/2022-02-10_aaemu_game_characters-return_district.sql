/*
 Navicat Premium Data Transfer

 Source Server         : archeage
 Source Server Type    : MySQL
 Source Server Version : 80012
 Source Host           : localhost:3306
 Source Schema         : aaemu_game12

 Target Server Type    : MySQL
 Target Server Version : 80012
 File Encoding         : 65001

 Date: 10/02/2022 21:34:13
*/

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

-- ----------------------------
-- Table structure for characters
-- ----------------------------
DROP TABLE IF EXISTS `characters`;
CREATE TABLE `characters`  (
  `id` int(10) UNSIGNED NOT NULL,
  `account_id` int(10) UNSIGNED NOT NULL,
  `name` varchar(128) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `access_level` int(10) UNSIGNED NOT NULL DEFAULT 0,
  `race` tinyint(4) NOT NULL,
  `gender` tinyint(1) NOT NULL,
  `unit_model_params` blob NOT NULL,
  `level` tinyint(4) NOT NULL,
  `expirience` int(11) NOT NULL,
  `recoverable_exp` int(11) NOT NULL,
  `hp` int(11) NOT NULL,
  `mp` int(11) NOT NULL,
  `labor_power` mediumint(9) NOT NULL,
  `labor_power_modified` datetime(0) NOT NULL DEFAULT '0001-01-01 00:00:00',
  `consumed_lp` int(11) NOT NULL,
  `ability1` tinyint(4) NOT NULL,
  `ability2` tinyint(4) NOT NULL,
  `ability3` tinyint(4) NOT NULL,
  `world_id` int(10) UNSIGNED NOT NULL,
  `zone_id` int(10) UNSIGNED NOT NULL,
  `x` float NOT NULL,
  `y` float NOT NULL,
  `z` float NOT NULL,
  `yaw` float NOT NULL DEFAULT 0,
  `pitch` float NOT NULL DEFAULT 0,
  `roll` float NOT NULL DEFAULT 0,
  `faction_id` int(10) UNSIGNED NOT NULL,
  `faction_name` varchar(128) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `expedition_id` int(11) NOT NULL,
  `family` int(10) UNSIGNED NOT NULL,
  `dead_count` mediumint(8) UNSIGNED NOT NULL,
  `dead_time` datetime(0) NOT NULL DEFAULT '0001-01-01 00:00:00',
  `rez_wait_duration` int(11) NOT NULL,
  `rez_time` datetime(0) NOT NULL DEFAULT '0001-01-01 00:00:00',
  `rez_penalty_duration` int(11) NOT NULL,
  `leave_time` datetime(0) NOT NULL DEFAULT '0001-01-01 00:00:00',
  `money` bigint(20) NOT NULL,
  `money2` bigint(20) NOT NULL,
  `honor_point` int(11) NOT NULL,
  `vocation_point` int(11) NOT NULL,
  `crime_point` int(11) NOT NULL,
  `crime_record` int(11) NOT NULL,
  `hostile_faction_kills` int(11) NOT NULL DEFAULT 0,
  `pvp_honor` int(11) NOT NULL DEFAULT 0,
  `delete_request_time` datetime(0) NOT NULL DEFAULT '0001-01-01 00:00:00',
  `transfer_request_time` datetime(0) NOT NULL DEFAULT '0001-01-01 00:00:00',
  `delete_time` datetime(0) NOT NULL DEFAULT '0001-01-01 00:00:00',
  `bm_point` int(11) NOT NULL,
  `auto_use_aapoint` tinyint(1) NOT NULL,
  `prev_point` int(11) NOT NULL,
  `point` int(11) NOT NULL,
  `gift` int(11) NOT NULL,
  `num_inv_slot` tinyint(3) UNSIGNED NOT NULL DEFAULT 50,
  `num_bank_slot` smallint(5) UNSIGNED NOT NULL DEFAULT 50,
  `expanded_expert` tinyint(4) NOT NULL,
  `slots` blob NOT NULL,
  `created_at` datetime(0) NOT NULL DEFAULT CURRENT_TIMESTAMP(0),
  `updated_at` datetime(0) NOT NULL DEFAULT '0001-01-01 00:00:00',
  `deleted` int(11) NOT NULL DEFAULT 0,
  `return_district` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`, `account_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci COMMENT = 'Basic player character data' ROW_FORMAT = DYNAMIC;

SET FOREIGN_KEY_CHECKS = 1;
