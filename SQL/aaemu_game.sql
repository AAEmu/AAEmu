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

 Date: 10/02/2022 21:34:37
*/

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

-- ----------------------------
-- Table structure for abilities
-- ----------------------------
DROP TABLE IF EXISTS `abilities`;
CREATE TABLE `abilities`  (
  `id` tinyint(3) UNSIGNED NOT NULL,
  `exp` int(11) NOT NULL,
  `owner` int(10) UNSIGNED NOT NULL,
  PRIMARY KEY (`id`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci COMMENT = 'Skillsets Exp' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for accounts
-- ----------------------------
DROP TABLE IF EXISTS `accounts`;
CREATE TABLE `accounts`  (
  `account_id` int(11) NOT NULL,
  `credits` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`account_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci COMMENT = 'Account specific values not related to login' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for actabilities
-- ----------------------------
DROP TABLE IF EXISTS `actabilities`;
CREATE TABLE `actabilities`  (
  `id` int(10) UNSIGNED NOT NULL,
  `point` int(10) UNSIGNED NOT NULL DEFAULT 0,
  `step` tinyint(3) UNSIGNED NOT NULL DEFAULT 0,
  `owner` int(10) UNSIGNED NOT NULL,
  PRIMARY KEY (`owner`, `id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci COMMENT = 'Vocations' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for appellations
-- ----------------------------
DROP TABLE IF EXISTS `appellations`;
CREATE TABLE `appellations`  (
  `id` int(10) UNSIGNED NOT NULL,
  `active` tinyint(1) NOT NULL DEFAULT 0,
  `owner` int(10) UNSIGNED NOT NULL,
  PRIMARY KEY (`id`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci COMMENT = 'Earned titles' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for auction_house
-- ----------------------------
DROP TABLE IF EXISTS `auction_house`;
CREATE TABLE `auction_house`  (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `duration` tinyint(4) NOT NULL,
  `item_id` int(11) NOT NULL,
  `object_id` int(11) NOT NULL,
  `grade` tinyint(1) NOT NULL,
  `flags` tinyint(1) NOT NULL,
  `stack_size` int(11) NOT NULL,
  `detail_type` tinyint(1) NOT NULL,
  `creation_time` datetime(0) NOT NULL,
  `end_time` datetime(0) NOT NULL,
  `lifespan_mins` int(11) NOT NULL,
  `type_1` int(11) NOT NULL,
  `world_id` tinyint(4) NOT NULL,
  `unsecure_date_time` varchar(45) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `unpack_date_time` varchar(45) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `world_id_2` tinyint(4) NOT NULL,
  `client_id` int(11) NOT NULL,
  `client_name` varchar(45) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `start_money` int(11) NOT NULL,
  `direct_money` int(11) NOT NULL,
  `bid_world_id` tinyint(1) NOT NULL,
  `bidder_id` int(11) NOT NULL,
  `bidder_name` varchar(45) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `bid_money` int(11) NOT NULL,
  `extra` int(11) NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 3 CHARACTER SET = utf8 COLLATE = utf8_general_ci ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for blocked
-- ----------------------------
DROP TABLE IF EXISTS `blocked`;
CREATE TABLE `blocked`  (
  `owner` int(11) NOT NULL,
  `blocked_id` int(11) NOT NULL,
  PRIMARY KEY (`owner`, `blocked_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for cash_shop_item
-- ----------------------------
DROP TABLE IF EXISTS `cash_shop_item`;
CREATE TABLE `cash_shop_item`  (
  `id` int(10) UNSIGNED NOT NULL AUTO_INCREMENT COMMENT 'shop_id',
  `uniq_id` int(10) UNSIGNED NULL DEFAULT 0 COMMENT '唯一ID',
  `cash_name` varchar(255) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL COMMENT '出售名称',
  `main_tab` tinyint(3) UNSIGNED NULL DEFAULT 1 COMMENT '主分类1-6',
  `sub_tab` tinyint(3) UNSIGNED NULL DEFAULT 1 COMMENT '子分类1-7',
  `level_min` tinyint(3) UNSIGNED NULL DEFAULT 0 COMMENT '等级限制',
  `level_max` tinyint(3) UNSIGNED NULL DEFAULT 0 COMMENT '等级限制',
  `item_template_id` int(10) UNSIGNED NULL DEFAULT 0 COMMENT '物品模板id',
  `is_sell` tinyint(1) UNSIGNED NULL DEFAULT 0 COMMENT '是否出售',
  `is_hidden` tinyint(1) UNSIGNED NULL DEFAULT 0 COMMENT '是否隐藏',
  `limit_type` tinyint(3) UNSIGNED NULL DEFAULT 0,
  `buy_count` smallint(5) UNSIGNED NULL DEFAULT 0,
  `buy_type` tinyint(3) UNSIGNED NULL DEFAULT 0,
  `buy_id` int(10) UNSIGNED NULL DEFAULT 0,
  `start_date` datetime(0) NULL DEFAULT '0001-01-01 00:00:00' COMMENT '出售开始',
  `end_date` datetime(0) NULL DEFAULT '0001-01-01 00:00:00' COMMENT '出售截止',
  `type` tinyint(3) UNSIGNED NULL DEFAULT 0 COMMENT '货币类型',
  `price` int(10) UNSIGNED NULL DEFAULT 0 COMMENT '价格',
  `remain` int(10) UNSIGNED NULL DEFAULT 0 COMMENT '剩余数量',
  `bonus_type` int(10) UNSIGNED NULL DEFAULT 0 COMMENT '赠送类型',
  `bouns_count` int(10) UNSIGNED NULL DEFAULT 0 COMMENT '赠送数量',
  `cmd_ui` tinyint(1) UNSIGNED NULL DEFAULT 0 COMMENT '是否限制一人一次',
  `item_count` int(10) UNSIGNED NULL DEFAULT 1 COMMENT '捆绑数量',
  `select_type` tinyint(3) UNSIGNED NULL DEFAULT 0,
  `default_flag` tinyint(3) UNSIGNED NULL DEFAULT 0,
  `event_type` tinyint(3) UNSIGNED NULL DEFAULT 0 COMMENT '活动类型',
  `event_date` datetime(0) NULL DEFAULT '0001-01-01 00:00:00' COMMENT '活动时间',
  `dis_price` int(10) UNSIGNED NULL DEFAULT 0 COMMENT '当前售价',
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 20100054 CHARACTER SET = utf8 COLLATE = utf8_general_ci COMMENT = '此表来自于代码中的字段并去除重复字段生成。字段名称和内容以代码为准。' ROW_FORMAT = DYNAMIC;

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

-- ----------------------------
-- Table structure for completed_quests
-- ----------------------------
DROP TABLE IF EXISTS `completed_quests`;
CREATE TABLE `completed_quests`  (
  `id` int(10) UNSIGNED NOT NULL,
  `data` tinyblob NOT NULL,
  `owner` int(10) UNSIGNED NOT NULL,
  PRIMARY KEY (`id`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci COMMENT = 'Quests marked as completed for character' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for doodads
-- ----------------------------
DROP TABLE IF EXISTS `doodads`;
CREATE TABLE `doodads`  (
  `id` int(10) UNSIGNED NOT NULL AUTO_INCREMENT,
  `owner_id` int(11) NULL DEFAULT NULL COMMENT 'Character DB Id',
  `owner_type` tinyint(3) UNSIGNED NULL DEFAULT 255,
  `template_id` int(11) NOT NULL,
  `current_phase_id` int(11) NOT NULL,
  `plant_time` datetime(0) NOT NULL,
  `growth_time` datetime(0) NOT NULL,
  `phase_time` datetime(0) NOT NULL,
  `x` float NOT NULL,
  `y` float NOT NULL,
  `z` float NOT NULL,
  `roll` float NOT NULL,
  `pitch` float NOT NULL,
  `yaw` float NOT NULL,
  `item_id` bigint(20) UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Item DB Id of the associated item',
  `house_id` int(10) UNSIGNED NOT NULL DEFAULT 0 COMMENT 'House DB Id if it is on actual house land',
  `parent_doodad` int(10) UNSIGNED NOT NULL DEFAULT 0 COMMENT 'doodads DB Id this object is standing on',
  `item_template_id` int(10) UNSIGNED NOT NULL DEFAULT 0 COMMENT 'ItemTemplateId of associated item',
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 4 CHARACTER SET = utf8 COLLATE = utf8_general_ci COMMENT = 'Persistent doodads (e.g. tradepacks, furniture)' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for expedition_members
-- ----------------------------
DROP TABLE IF EXISTS `expedition_members`;
CREATE TABLE `expedition_members`  (
  `character_id` int(11) NOT NULL,
  `expedition_id` int(11) NOT NULL,
  `name` varchar(128) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `level` tinyint(4) UNSIGNED NOT NULL,
  `role` tinyint(4) UNSIGNED NOT NULL,
  `last_leave_time` datetime(0) NOT NULL DEFAULT CURRENT_TIMESTAMP(0),
  `ability1` tinyint(4) UNSIGNED NOT NULL,
  `ability2` tinyint(4) UNSIGNED NOT NULL,
  `ability3` tinyint(4) UNSIGNED NOT NULL,
  `memo` varchar(128) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  PRIMARY KEY (`character_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for expedition_role_policies
-- ----------------------------
DROP TABLE IF EXISTS `expedition_role_policies`;
CREATE TABLE `expedition_role_policies`  (
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
  PRIMARY KEY (`expedition_id`, `role`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for expeditions
-- ----------------------------
DROP TABLE IF EXISTS `expeditions`;
CREATE TABLE `expeditions`  (
  `id` int(11) NOT NULL,
  `owner` int(11) NOT NULL,
  `owner_name` varchar(128) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `name` varchar(128) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `mother` int(11) NOT NULL,
  `created_at` datetime(0) NOT NULL DEFAULT CURRENT_TIMESTAMP(0),
  PRIMARY KEY (`id`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for family_members
-- ----------------------------
DROP TABLE IF EXISTS `family_members`;
CREATE TABLE `family_members`  (
  `character_id` int(11) NOT NULL,
  `family_id` int(11) NOT NULL,
  `name` varchar(45) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `role` tinyint(1) NOT NULL DEFAULT 0,
  `title` varchar(45) CHARACTER SET utf8 COLLATE utf8_general_ci NULL DEFAULT NULL,
  PRIMARY KEY (`family_id`, `character_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci COMMENT = 'Family members' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for friends
-- ----------------------------
DROP TABLE IF EXISTS `friends`;
CREATE TABLE `friends`  (
  `id` int(11) NOT NULL,
  `friend_id` int(11) NOT NULL,
  `owner` int(11) NOT NULL,
  PRIMARY KEY (`id`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for housings
-- ----------------------------
DROP TABLE IF EXISTS `housings`;
CREATE TABLE `housings`  (
  `id` int(11) NOT NULL,
  `account_id` int(10) UNSIGNED NOT NULL,
  `owner` int(10) UNSIGNED NOT NULL,
  `co_owner` int(10) UNSIGNED NOT NULL,
  `template_id` int(10) UNSIGNED NOT NULL,
  `name` varchar(128) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `x` float NOT NULL,
  `y` float NOT NULL,
  `z` float NOT NULL,
  `rotation_z` tinyint(4) NOT NULL,
  `current_step` tinyint(4) NOT NULL,
  `current_action` int(11) NOT NULL DEFAULT 0,
  `permission` tinyint(4) NOT NULL,
  `place_date` datetime(0) NOT NULL DEFAULT CURRENT_TIMESTAMP(0),
  `protected_until` datetime(0) NOT NULL DEFAULT CURRENT_TIMESTAMP(0),
  `faction_id` int(10) UNSIGNED NOT NULL DEFAULT 1,
  `sell_to` int(10) UNSIGNED NOT NULL DEFAULT 0,
  `sell_price` bigint(20) NOT NULL DEFAULT 0,
  `allow_recover` tinyint(3) UNSIGNED NOT NULL DEFAULT 1,
  PRIMARY KEY (`account_id`, `owner`, `id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for items
-- ----------------------------
DROP TABLE IF EXISTS `items`;
CREATE TABLE `items`  (
  `id` bigint(20) UNSIGNED NOT NULL,
  `type` varchar(100) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `template_id` int(10) UNSIGNED NOT NULL,
  `slot_type` enum('Equipment','Inventory','Bank','Trade','Mail') CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `slot` int(11) NOT NULL,
  `count` int(11) NOT NULL,
  `details` blob NULL,
  `lifespan_mins` int(11) NOT NULL,
  `made_unit_id` int(10) UNSIGNED NOT NULL DEFAULT 0,
  `unsecure_time` datetime(0) NOT NULL DEFAULT '0001-01-01 00:00:00',
  `unpack_time` datetime(0) NOT NULL DEFAULT '0001-01-01 00:00:00',
  `owner` int(10) UNSIGNED NOT NULL,
  `grade` tinyint(1) NULL DEFAULT 0,
  `flags` tinyint(3) UNSIGNED NOT NULL,
  `created_at` datetime(0) NOT NULL DEFAULT '0001-01-01 00:00:00',
  `bounded` tinyint(1) NULL DEFAULT 0,
  `ucc` int(10) UNSIGNED NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`) USING BTREE,
  INDEX `owner`(`owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci COMMENT = 'All items' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for mails
-- ----------------------------
DROP TABLE IF EXISTS `mails`;
CREATE TABLE `mails`  (
  `id` int(11) NOT NULL,
  `type` int(11) NOT NULL,
  `status` int(11) NOT NULL,
  `title` varchar(45) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `text` varchar(150) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `sender_id` int(11) NOT NULL DEFAULT 0,
  `sender_name` varchar(45) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `attachment_count` int(11) NOT NULL DEFAULT 0,
  `receiver_id` int(11) NOT NULL DEFAULT 0,
  `receiver_name` varchar(45) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `open_date` datetime(0) NOT NULL,
  `send_date` datetime(0) NOT NULL,
  `received_date` datetime(0) NOT NULL,
  `returned` int(11) NOT NULL,
  `extra` bigint(20) NOT NULL,
  `money_amount_1` int(11) NOT NULL,
  `money_amount_2` int(11) NOT NULL,
  `money_amount_3` int(11) NOT NULL,
  `attachment0` bigint(20) NOT NULL DEFAULT 0,
  `attachment1` bigint(20) NOT NULL DEFAULT 0,
  `attachment2` bigint(20) NOT NULL DEFAULT 0,
  `attachment3` bigint(20) NOT NULL DEFAULT 0,
  `attachment4` bigint(20) NOT NULL DEFAULT 0,
  `attachment5` bigint(20) NOT NULL DEFAULT 0,
  `attachment6` bigint(20) NOT NULL DEFAULT 0,
  `attachment7` bigint(20) NOT NULL DEFAULT 0,
  `attachment8` bigint(20) NOT NULL DEFAULT 0,
  `attachment9` bigint(20) NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for mates
-- ----------------------------
DROP TABLE IF EXISTS `mates`;
CREATE TABLE `mates`  (
  `id` int(11) UNSIGNED NOT NULL,
  `item_id` bigint(20) UNSIGNED NOT NULL,
  `name` text CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `xp` int(11) NOT NULL,
  `level` tinyint(4) NOT NULL,
  `mileage` int(11) NOT NULL,
  `hp` int(11) NOT NULL,
  `mp` int(11) NOT NULL,
  `owner` int(11) UNSIGNED NOT NULL,
  `updated_at` datetime(0) NOT NULL DEFAULT CURRENT_TIMESTAMP(0),
  `created_at` datetime(0) NOT NULL DEFAULT CURRENT_TIMESTAMP(0),
  PRIMARY KEY (`id`, `item_id`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for music
-- ----------------------------
DROP TABLE IF EXISTS `music`;
CREATE TABLE `music`  (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `author` int(11) NOT NULL COMMENT 'PlayerId',
  `title` varchar(128) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `song` text CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL COMMENT 'Song MML',
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8 COLLATE = utf8_general_ci COMMENT = 'User Created Content (music)' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for options
-- ----------------------------
DROP TABLE IF EXISTS `options`;
CREATE TABLE `options`  (
  `key` varchar(100) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `value` text CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `owner` int(10) UNSIGNED NOT NULL,
  PRIMARY KEY (`key`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci COMMENT = 'Settings that the client stores on the server' ROW_FORMAT = DYNAMIC;

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
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for portal_visited_district
-- ----------------------------
DROP TABLE IF EXISTS `portal_visited_district`;
CREATE TABLE `portal_visited_district`  (
  `id` int(11) NOT NULL,
  `subzone` int(11) NOT NULL,
  `owner` int(11) NOT NULL,
  PRIMARY KEY (`id`, `subzone`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for quests
-- ----------------------------
DROP TABLE IF EXISTS `quests`;
CREATE TABLE `quests`  (
  `id` int(10) UNSIGNED NOT NULL,
  `template_id` int(10) UNSIGNED NOT NULL,
  `data` tinyblob NOT NULL,
  `status` tinyint(4) NOT NULL,
  `owner` int(10) UNSIGNED NOT NULL,
  PRIMARY KEY (`id`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci COMMENT = 'Currently open quests' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for skills
-- ----------------------------
DROP TABLE IF EXISTS `skills`;
CREATE TABLE `skills`  (
  `id` int(10) UNSIGNED NOT NULL,
  `level` tinyint(4) NOT NULL,
  `type` enum('Skill','Buff') CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `owner` int(10) UNSIGNED NOT NULL,
  PRIMARY KEY (`id`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci COMMENT = 'Learned character skills' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for uccs
-- ----------------------------
DROP TABLE IF EXISTS `uccs`;
CREATE TABLE `uccs`  (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `uploader_id` int(11) NOT NULL,
  `type` tinyint(3) NOT NULL,
  `data` blob NULL,
  `pattern1` int(11) UNSIGNED NOT NULL,
  `pattern2` int(11) UNSIGNED NOT NULL,
  `color1R` int(11) UNSIGNED NOT NULL,
  `color1G` int(11) UNSIGNED NOT NULL,
  `color1B` int(11) UNSIGNED NOT NULL,
  `color2R` int(11) UNSIGNED NOT NULL,
  `color2G` int(11) UNSIGNED NOT NULL,
  `color2B` int(11) UNSIGNED NOT NULL,
  `color3R` int(11) UNSIGNED NOT NULL,
  `color3G` int(11) UNSIGNED NOT NULL,
  `color3B` int(11) UNSIGNED NOT NULL,
  `modified` datetime(0) NOT NULL DEFAULT '0001-01-01 00:00:00',
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 3 CHARACTER SET = utf8 COLLATE = utf8_general_ci ROW_FORMAT = DYNAMIC;

SET FOREIGN_KEY_CHECKS = 1;
