/*
 Navicat Premium Data Transfer

 Source Server         : archeage
 Source Server Type    : MySQL
 Source Server Version : 80033
 Source Host           : localhost:3306
 Source Schema         : aaemu_game_3030

 Target Server Type    : MySQL
 Target Server Version : 80033
 File Encoding         : 65001

 Date: 30/04/2024 02:48:39
*/

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

-- ----------------------------
-- Table structure for abilities
-- ----------------------------
DROP TABLE IF EXISTS `abilities`;
CREATE TABLE `abilities`  (
  `id` tinyint UNSIGNED NOT NULL,
  `exp` int NOT NULL,
  `owner` int UNSIGNED NOT NULL,
  PRIMARY KEY (`id`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci COMMENT = 'Skillsets Exp' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of abilities
-- ----------------------------

-- ----------------------------
-- Table structure for accounts
-- ----------------------------
DROP TABLE IF EXISTS `accounts`;
CREATE TABLE `accounts`  (
  `account_id` int NOT NULL,
  `credits` int NOT NULL DEFAULT 0,
  PRIMARY KEY (`account_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci COMMENT = 'Account specific values not related to login' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of accounts
-- ----------------------------

-- ----------------------------
-- Table structure for actabilities
-- ----------------------------
DROP TABLE IF EXISTS `actabilities`;
CREATE TABLE `actabilities`  (
  `id` int UNSIGNED NOT NULL,
  `point` int UNSIGNED NOT NULL DEFAULT 0,
  `step` tinyint UNSIGNED NOT NULL DEFAULT 0,
  `owner` int UNSIGNED NOT NULL,
  PRIMARY KEY (`owner`, `id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci COMMENT = 'Vocations' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of actabilities
-- ----------------------------

-- ----------------------------
-- Table structure for appellations
-- ----------------------------
DROP TABLE IF EXISTS `appellations`;
CREATE TABLE `appellations`  (
  `id` int UNSIGNED NOT NULL,
  `active` tinyint(1) NOT NULL DEFAULT 0,
  `owner` int UNSIGNED NOT NULL,
  PRIMARY KEY (`id`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci COMMENT = 'Earned titles' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of appellations
-- ----------------------------

-- ----------------------------
-- Table structure for auction_house
-- ----------------------------
DROP TABLE IF EXISTS `auction_house`;
CREATE TABLE `auction_house`  (
  `id` int NOT NULL AUTO_INCREMENT,
  `duration` tinyint NOT NULL,
  `item_id` int NOT NULL,
  `object_id` int NOT NULL,
  `grade` tinyint(1) NOT NULL,
  `flags` tinyint(1) NOT NULL,
  `stack_size` int NOT NULL,
  `detail_type` tinyint(1) NOT NULL,
  `creation_time` datetime NOT NULL,
  `end_time` datetime NOT NULL,
  `lifespan_mins` int NOT NULL,
  `type_1` int NOT NULL,
  `world_id` tinyint NOT NULL,
  `unsecure_date_time` varchar(45) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `unpack_date_time` varchar(45) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `world_id_2` tinyint NOT NULL,
  `client_id` int NOT NULL,
  `client_name` varchar(45) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `start_money` int NOT NULL,
  `direct_money` int NOT NULL,
  `bid_world_id` tinyint(1) NOT NULL,
  `bidder_id` int NOT NULL,
  `bidder_name` varchar(45) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `bid_money` int NOT NULL,
  `extra` int NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci COMMENT = 'Listed AH Items' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of auction_house
-- ----------------------------

-- ----------------------------
-- Table structure for audit_ics_sales
-- ----------------------------
DROP TABLE IF EXISTS `audit_ics_sales`;
CREATE TABLE `audit_ics_sales`  (
  `id` bigint UNSIGNED NOT NULL AUTO_INCREMENT,
  `buyer_account` int UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Account ID of the person buying this item',
  `buyer_char` int UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Character that was logged in when buying',
  `target_account` int UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Account of the person receiving the goods',
  `target_char` int UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Character that received the goods',
  `sale_date` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Time of purchase (in UTC)',
  `shop_item_id` int UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Shop item entry id of the sold item',
  `sku` int UNSIGNED NOT NULL DEFAULT 0 COMMENT 'SKU of the sold item',
  `sale_cost` int NOT NULL DEFAULT 0 COMMENT 'Amount this item was sold for',
  `sale_currency` tinyint UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Which currency was used',
  `description` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT 'Added description of this transaction',
  PRIMARY KEY (`id`) USING BTREE,
  INDEX `buyer_account`(`buyer_account`) USING BTREE,
  INDEX `buyer_char`(`buyer_char`) USING BTREE,
  INDEX `target_account`(`target_account`) USING BTREE,
  INDEX `target_char`(`target_char`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Sales history for the ICS' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of audit_ics_sales
-- ----------------------------

-- ----------------------------
-- Table structure for blocked
-- ----------------------------
DROP TABLE IF EXISTS `blocked`;
CREATE TABLE `blocked`  (
  `owner` int NOT NULL,
  `blocked_id` int NOT NULL,
  PRIMARY KEY (`owner`, `blocked_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of blocked
-- ----------------------------

-- ----------------------------
-- Table structure for characters
-- ----------------------------
DROP TABLE IF EXISTS `characters`;
CREATE TABLE `characters`  (
  `id` int UNSIGNED NOT NULL,
  `account_id` int UNSIGNED NOT NULL,
  `name` varchar(128) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `access_level` int UNSIGNED NOT NULL DEFAULT 0,
  `race` tinyint NOT NULL,
  `gender` tinyint(1) NOT NULL,
  `unit_model_params` blob NOT NULL,
  `level` tinyint NOT NULL,
  `experience` int NOT NULL,
  `recoverable_exp` int NOT NULL,
  `hp` int NOT NULL,
  `mp` int NOT NULL,
  `labor_power` mediumint NOT NULL,
  `labor_power_modified` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `consumed_lp` int NOT NULL,
  `ability1` tinyint NOT NULL,
  `ability2` tinyint NOT NULL,
  `ability3` tinyint NOT NULL,
  `world_id` int UNSIGNED NOT NULL,
  `zone_id` int UNSIGNED NOT NULL,
  `x` float NOT NULL,
  `y` float NOT NULL,
  `z` float NOT NULL,
  `yaw` float NOT NULL DEFAULT 0,
  `pitch` float NOT NULL DEFAULT 0,
  `roll` float NOT NULL DEFAULT 0,
  `faction_id` int UNSIGNED NOT NULL,
  `faction_name` varchar(128) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `expedition_id` int NOT NULL,
  `family` int UNSIGNED NOT NULL,
  `dead_count` mediumint UNSIGNED NOT NULL,
  `dead_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `rez_wait_duration` int NOT NULL,
  `rez_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `rez_penalty_duration` int NOT NULL,
  `leave_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `money` bigint NOT NULL,
  `money2` bigint NOT NULL,
  `honor_point` int NOT NULL,
  `vocation_point` int NOT NULL,
  `crime_point` int NOT NULL,
  `crime_record` int NOT NULL,
  `hostile_faction_kills` int NOT NULL DEFAULT 0,
  `pvp_honor` int NOT NULL DEFAULT 0,
  `delete_request_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `transfer_request_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `delete_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `bm_point` int NOT NULL,
  `auto_use_aapoint` tinyint(1) NOT NULL,
  `prev_point` int NOT NULL,
  `point` int NOT NULL,
  `gift` int NOT NULL,
  `num_inv_slot` tinyint UNSIGNED NOT NULL DEFAULT 50,
  `num_bank_slot` smallint UNSIGNED NOT NULL DEFAULT 50,
  `expanded_expert` tinyint NOT NULL,
  `slots` blob NOT NULL,
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `deleted` int NOT NULL DEFAULT 0,
  `return_district` int NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`, `account_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci COMMENT = 'Basic player character data' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of characters
-- ----------------------------

-- ----------------------------
-- Table structure for completed_quests
-- ----------------------------
DROP TABLE IF EXISTS `completed_quests`;
CREATE TABLE `completed_quests`  (
  `id` int UNSIGNED NOT NULL,
  `data` tinyblob NOT NULL,
  `owner` int UNSIGNED NOT NULL,
  PRIMARY KEY (`id`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci COMMENT = 'Quests marked as completed for character' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of completed_quests
-- ----------------------------

-- ----------------------------
-- Table structure for doodads
-- ----------------------------
DROP TABLE IF EXISTS `doodads`;
CREATE TABLE `doodads`  (
  `id` int UNSIGNED NOT NULL AUTO_INCREMENT,
  `owner_id` int NULL DEFAULT NULL COMMENT 'Character DB Id',
  `owner_type` tinyint UNSIGNED NULL DEFAULT 255,
  `attach_point` int UNSIGNED NULL DEFAULT 0 COMMENT 'Slot this doodad fits in on the owner',
  `template_id` int NOT NULL,
  `current_phase_id` int NOT NULL,
  `plant_time` datetime NOT NULL,
  `growth_time` datetime NOT NULL,
  `phase_time` datetime NOT NULL,
  `x` float NOT NULL,
  `y` float NOT NULL,
  `z` float NOT NULL,
  `roll` float NOT NULL,
  `pitch` float NOT NULL,
  `yaw` float NOT NULL,
  `scale` float NOT NULL DEFAULT 1,
  `item_id` bigint UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Item DB Id of the associated item',
  `house_id` int UNSIGNED NOT NULL DEFAULT 0 COMMENT 'House DB Id if it is on actual house land',
  `parent_doodad` int UNSIGNED NOT NULL DEFAULT 0 COMMENT 'doodads DB Id this object is standing on',
  `item_template_id` int UNSIGNED NOT NULL DEFAULT 0 COMMENT 'ItemTemplateId of associated item',
  `item_container_id` int UNSIGNED NOT NULL DEFAULT 0 COMMENT 'ItemContainer Id for Coffers',
  `data` int NOT NULL DEFAULT 0 COMMENT 'Doodad specific data',
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 30 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci COMMENT = 'Persistent doodads (e.g. tradepacks, furniture)' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of doodads
-- ----------------------------

-- ----------------------------
-- Table structure for expedition_members
-- ----------------------------
DROP TABLE IF EXISTS `expedition_members`;
CREATE TABLE `expedition_members`  (
  `character_id` int NOT NULL,
  `expedition_id` int NOT NULL,
  `name` varchar(128) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `level` tinyint UNSIGNED NOT NULL,
  `role` tinyint UNSIGNED NOT NULL,
  `last_leave_time` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `ability1` tinyint UNSIGNED NOT NULL,
  `ability2` tinyint UNSIGNED NOT NULL,
  `ability3` tinyint UNSIGNED NOT NULL,
  `memo` varchar(128) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  PRIMARY KEY (`character_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci COMMENT = 'Guild members' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of expedition_members
-- ----------------------------

-- ----------------------------
-- Table structure for expedition_role_policies
-- ----------------------------
DROP TABLE IF EXISTS `expedition_role_policies`;
CREATE TABLE `expedition_role_policies`  (
  `expedition_id` int NOT NULL,
  `role` tinyint UNSIGNED NOT NULL,
  `name` varchar(128) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
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
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci COMMENT = 'Guild role settings' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of expedition_role_policies
-- ----------------------------

-- ----------------------------
-- Table structure for expeditions
-- ----------------------------
DROP TABLE IF EXISTS `expeditions`;
CREATE TABLE `expeditions`  (
  `id` int NOT NULL,
  `owner` int NOT NULL,
  `owner_name` varchar(128) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `name` varchar(128) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `mother` int NOT NULL,
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci COMMENT = 'Guilds' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of expeditions
-- ----------------------------

-- ----------------------------
-- Table structure for family_members
-- ----------------------------
DROP TABLE IF EXISTS `family_members`;
CREATE TABLE `family_members`  (
  `character_id` int NOT NULL,
  `family_id` int NOT NULL,
  `name` varchar(45) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `role` tinyint(1) NOT NULL DEFAULT 0,
  `title` varchar(45) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL DEFAULT NULL,
  PRIMARY KEY (`family_id`, `character_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci COMMENT = 'Family members' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of family_members
-- ----------------------------

-- ----------------------------
-- Table structure for friends
-- ----------------------------
DROP TABLE IF EXISTS `friends`;
CREATE TABLE `friends`  (
  `id` int NOT NULL,
  `friend_id` int NOT NULL,
  `owner` int NOT NULL,
  PRIMARY KEY (`id`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci COMMENT = 'Friendslist' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of friends
-- ----------------------------

-- ----------------------------
-- Table structure for housings
-- ----------------------------
DROP TABLE IF EXISTS `housings`;
CREATE TABLE `housings`  (
  `id` int NOT NULL,
  `account_id` int UNSIGNED NOT NULL,
  `owner` int UNSIGNED NOT NULL,
  `co_owner` int UNSIGNED NOT NULL,
  `template_id` int UNSIGNED NOT NULL,
  `name` varchar(128) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `x` float NOT NULL,
  `y` float NOT NULL,
  `z` float NOT NULL,
  `yaw` float NOT NULL DEFAULT 0,
  `pitch` float NOT NULL DEFAULT 0,
  `roll` float NOT NULL DEFAULT 0,
  `current_step` tinyint NOT NULL,
  `current_action` int NOT NULL DEFAULT 0,
  `permission` tinyint NOT NULL,
  `place_date` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `protected_until` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `faction_id` int UNSIGNED NOT NULL DEFAULT 1,
  `sell_to` int UNSIGNED NOT NULL DEFAULT 0,
  `sell_price` bigint NOT NULL DEFAULT 0,
  `allow_recover` tinyint UNSIGNED NOT NULL DEFAULT 1,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci COMMENT = 'Player buildings' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of housings
-- ----------------------------

-- ----------------------------
-- Table structure for ics_menu
-- ----------------------------
DROP TABLE IF EXISTS `ics_menu`;
CREATE TABLE `ics_menu`  (
  `id` int NOT NULL AUTO_INCREMENT,
  `main_tab` tinyint UNSIGNED NOT NULL DEFAULT 1 COMMENT 'Which main tab to display on',
  `sub_tab` tinyint UNSIGNED NOT NULL DEFAULT 1 COMMENT 'Which sub tab to display on',
  `tab_pos` int NOT NULL DEFAULT 0 COMMENT 'Used to change display order',
  `shop_id` int UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Id of the item group for sale (shop item)',
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Contains what item will be displayed on which tab' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of ics_menu
-- ----------------------------

-- ----------------------------
-- Table structure for ics_shop_items
-- ----------------------------
DROP TABLE IF EXISTS `ics_shop_items`;
CREATE TABLE `ics_shop_items`  (
  `shop_id` int UNSIGNED NOT NULL AUTO_INCREMENT COMMENT 'SKU item id',
  `display_item_id` int UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Item who\'s icon to use for displaying in the shop, leave 0 for first item in the group',
  `name` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL COMMENT 'Can be used to override the name in the shop',
  `limited_type` tinyint UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Enables limited stock mode if non-zero, Account(1), Chracter(2)',
  `limited_stock_max` int UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Number of items left in stock for this SKU if limited stock is enabled',
  `level_min` tinyint UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Minimum level to buy the item (does not show on UI)',
  `level_max` tinyint UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Maximum level to buy the item (does not show on UI)',
  `buy_restrict_type` tinyint UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Buy restriction rule, none (0), level (1) or quest(2)',
  `buy_restrict_id` int UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Level or QuestId for restrict rule',
  `is_sale` tinyint UNSIGNED NOT NULL DEFAULT 0,
  `is_hidden` tinyint UNSIGNED NOT NULL DEFAULT 0,
  `sale_start` datetime NULL DEFAULT NULL COMMENT 'Limited sale start time',
  `sale_end` datetime NULL DEFAULT NULL COMMENT 'Limited sale end time',
  `shop_buttons` tinyint UNSIGNED NOT NULL DEFAULT 0 COMMENT 'All (0), NoCart (1), NoGift (2), OnlyBuy (3)',
  `remaining` int NOT NULL DEFAULT -1 COMMENT 'Number of items remaining, only for tab 1-1 (limited)',
  PRIMARY KEY (`shop_id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Possible Item listings that are for sale' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of ics_shop_items
-- ----------------------------

-- ----------------------------
-- Table structure for ics_skus
-- ----------------------------
DROP TABLE IF EXISTS `ics_skus`;
CREATE TABLE `ics_skus`  (
  `sku` int UNSIGNED NOT NULL AUTO_INCREMENT,
  `shop_id` int UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Reference to the shop item',
  `position` int NOT NULL DEFAULT 0 COMMENT 'Used for display order inside the item details',
  `item_id` int UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Item that is for sale',
  `item_count` int UNSIGNED NOT NULL DEFAULT 1 COMMENT 'Number of items for this detail',
  `select_type` tinyint UNSIGNED NOT NULL DEFAULT 0,
  `is_default` tinyint UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Is this the default selection?',
  `event_type` tinyint UNSIGNED NOT NULL DEFAULT 0,
  `event_end_date` datetime NULL DEFAULT NULL,
  `currency` tinyint UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Credits(0), AAPoints(1), Loyalty(2), Coins(3)',
  `price` int UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Price of the item',
  `discount_price` int UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Discounted price (this is used if set)',
  `bonus_item_id` int UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Bonus item included for this purchase',
  `bonus_item_count` int UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Amount of bonus items included',
  `pay_item_type` int UNSIGNED NOT NULL DEFAULT 0,
  PRIMARY KEY (`sku`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Has the actual sales items for the details' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of ics_skus
-- ----------------------------

-- ----------------------------
-- Table structure for item_containers
-- ----------------------------
DROP TABLE IF EXISTS `item_containers`;
CREATE TABLE `item_containers`  (
  `container_id` int UNSIGNED NOT NULL,
  `container_type` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL DEFAULT 'ItemContainer' COMMENT 'Partial Container Class Name',
  `slot_type` enum('Equipment','Inventory','Bank','Trade','Mail','System','EquipmentMate') CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT 'Internal Container Type',
  `container_size` int NOT NULL DEFAULT 50 COMMENT 'Maximum Container Size',
  `owner_id` int UNSIGNED NOT NULL COMMENT 'Owning Character Id',
  `mate_id` int UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Owning Mate Id',
  PRIMARY KEY (`container_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of item_containers
-- ----------------------------

-- ----------------------------
-- Table structure for items
-- ----------------------------
DROP TABLE IF EXISTS `items`;
CREATE TABLE `items`  (
  `id` bigint UNSIGNED NOT NULL,
  `type` varchar(100) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `template_id` int UNSIGNED NOT NULL,
  `container_id` int UNSIGNED NOT NULL DEFAULT 0,
  `slot_type` enum('Equipment','Inventory','Bank','Trade','Mail','System','EquipmentMate') CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT 'Internal Container Type',
  `slot` int NOT NULL,
  `count` int NOT NULL,
  `details` blob NULL,
  `lifespan_mins` int NOT NULL,
  `made_unit_id` int UNSIGNED NOT NULL DEFAULT 0,
  `unsecure_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `unpack_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `owner` int UNSIGNED NOT NULL,
  `grade` tinyint(1) NULL DEFAULT 0,
  `flags` tinyint UNSIGNED NOT NULL,
  `created_at` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `ucc` int UNSIGNED NOT NULL DEFAULT 0,
  `expire_time` datetime NULL DEFAULT NULL COMMENT 'Fixed time expire',
  `expire_online_minutes` double NOT NULL DEFAULT 0 COMMENT 'Time left when player online',
  `charge_time` datetime NULL DEFAULT NULL COMMENT 'Time charged items got activated',
  `charge_count` int NOT NULL DEFAULT 0 COMMENT 'Number of charges left',
  PRIMARY KEY (`id`) USING BTREE,
  INDEX `owner`(`owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci COMMENT = 'All items' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of items
-- ----------------------------

-- ----------------------------
-- Table structure for mails
-- ----------------------------
DROP TABLE IF EXISTS `mails`;
CREATE TABLE `mails`  (
  `id` int NOT NULL,
  `type` int NOT NULL,
  `status` int NOT NULL,
  `title` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `text` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `sender_id` int NOT NULL DEFAULT 0,
  `sender_name` varchar(45) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `attachment_count` int NOT NULL DEFAULT 0,
  `receiver_id` int NOT NULL DEFAULT 0,
  `receiver_name` varchar(45) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `open_date` datetime NOT NULL,
  `send_date` datetime NOT NULL,
  `received_date` datetime NOT NULL,
  `returned` int NOT NULL,
  `extra` bigint NOT NULL,
  `money_amount_1` int NOT NULL,
  `money_amount_2` int NOT NULL,
  `money_amount_3` int NOT NULL,
  `attachment0` bigint NOT NULL DEFAULT 0,
  `attachment1` bigint NOT NULL DEFAULT 0,
  `attachment2` bigint NOT NULL DEFAULT 0,
  `attachment3` bigint NOT NULL DEFAULT 0,
  `attachment4` bigint NOT NULL DEFAULT 0,
  `attachment5` bigint NOT NULL DEFAULT 0,
  `attachment6` bigint NOT NULL DEFAULT 0,
  `attachment7` bigint NOT NULL DEFAULT 0,
  `attachment8` bigint NOT NULL DEFAULT 0,
  `attachment9` bigint NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci COMMENT = 'In-game mails' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of mails
-- ----------------------------

-- ----------------------------
-- Table structure for mates
-- ----------------------------
DROP TABLE IF EXISTS `mates`;
CREATE TABLE `mates`  (
  `id` int UNSIGNED NOT NULL,
  `item_id` bigint UNSIGNED NOT NULL,
  `name` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `xp` int NOT NULL,
  `level` tinyint NOT NULL,
  `mileage` int NOT NULL,
  `hp` int NOT NULL,
  `mp` int NOT NULL,
  `owner` int UNSIGNED NOT NULL,
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`, `item_id`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci COMMENT = 'Player mounts and pets' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of mates
-- ----------------------------

-- ----------------------------
-- Table structure for music
-- ----------------------------
DROP TABLE IF EXISTS `music`;
CREATE TABLE `music`  (
  `id` int NOT NULL AUTO_INCREMENT,
  `author` int NOT NULL COMMENT 'PlayerId',
  `title` varchar(128) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `song` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL COMMENT 'Song MML',
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci COMMENT = 'User Created Content (music)' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of music
-- ----------------------------

-- ----------------------------
-- Table structure for options
-- ----------------------------
DROP TABLE IF EXISTS `options`;
CREATE TABLE `options`  (
  `key` varchar(100) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `value` text CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `owner` int UNSIGNED NOT NULL,
  PRIMARY KEY (`key`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci COMMENT = 'Settings that the client stores on the server' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of options
-- ----------------------------

-- ----------------------------
-- Table structure for portal_book_coords
-- ----------------------------
DROP TABLE IF EXISTS `portal_book_coords`;
CREATE TABLE `portal_book_coords`  (
  `id` int NOT NULL,
  `name` varchar(128) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `x` int NULL DEFAULT 0,
  `y` int NULL DEFAULT 0,
  `z` int NULL DEFAULT 0,
  `zone_id` int NULL DEFAULT 0,
  `z_rot` int NULL DEFAULT 0,
  `sub_zone_id` int NULL DEFAULT 0,
  `owner` int NOT NULL,
  PRIMARY KEY (`id`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci COMMENT = 'Recorded house portals in the portal book' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of portal_book_coords
-- ----------------------------

-- ----------------------------
-- Table structure for portal_visited_district
-- ----------------------------
DROP TABLE IF EXISTS `portal_visited_district`;
CREATE TABLE `portal_visited_district`  (
  `id` int NOT NULL,
  `subzone` int NOT NULL,
  `owner` int NOT NULL,
  PRIMARY KEY (`id`, `subzone`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci COMMENT = 'List of visited area for the portal book' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of portal_visited_district
-- ----------------------------

-- ----------------------------
-- Table structure for quests
-- ----------------------------
DROP TABLE IF EXISTS `quests`;
CREATE TABLE `quests`  (
  `id` int UNSIGNED NOT NULL,
  `template_id` int UNSIGNED NOT NULL,
  `data` tinyblob NOT NULL,
  `status` tinyint NOT NULL,
  `owner` int UNSIGNED NOT NULL,
  PRIMARY KEY (`id`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci COMMENT = 'Currently open quests' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of quests
-- ----------------------------

-- ----------------------------
-- Table structure for skills
-- ----------------------------
DROP TABLE IF EXISTS `skills`;
CREATE TABLE `skills`  (
  `id` int UNSIGNED NOT NULL,
  `level` tinyint NOT NULL,
  `type` enum('Skill','Buff') CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `owner` int UNSIGNED NOT NULL,
  PRIMARY KEY (`id`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci COMMENT = 'Learned character skills' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of skills
-- ----------------------------

-- ----------------------------
-- Table structure for slaves
-- ----------------------------
DROP TABLE IF EXISTS `slaves`;
CREATE TABLE `slaves`  (
  `id` int UNSIGNED NOT NULL,
  `item_id` int UNSIGNED NULL DEFAULT NULL COMMENT 'Item that is used to summon this vehicle',
  `name` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL,
  `owner` int UNSIGNED NULL DEFAULT NULL,
  `created_at` datetime NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` datetime NULL DEFAULT CURRENT_TIMESTAMP,
  `hp` int NULL DEFAULT NULL,
  `mp` int NULL DEFAULT NULL,
  `x` float NULL DEFAULT NULL,
  `y` float NULL DEFAULT NULL,
  `z` float NULL DEFAULT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Player vehicles summons' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of slaves
-- ----------------------------

-- ----------------------------
-- Table structure for uccs
-- ----------------------------
DROP TABLE IF EXISTS `uccs`;
CREATE TABLE `uccs`  (
  `id` int NOT NULL AUTO_INCREMENT,
  `uploader_id` int NOT NULL COMMENT 'PlayerID',
  `type` tinyint NOT NULL,
  `data` mediumblob NULL COMMENT 'Raw uploaded UCC data',
  `pattern1` int UNSIGNED NOT NULL COMMENT 'Background pattern',
  `pattern2` int UNSIGNED NOT NULL COMMENT 'Crest',
  `color1R` int UNSIGNED NOT NULL,
  `color1G` int UNSIGNED NOT NULL,
  `color1B` int UNSIGNED NOT NULL,
  `color2R` int UNSIGNED NOT NULL,
  `color2G` int UNSIGNED NOT NULL,
  `color2B` int UNSIGNED NOT NULL,
  `color3R` int UNSIGNED NOT NULL,
  `color3G` int UNSIGNED NOT NULL,
  `color3B` int UNSIGNED NOT NULL,
  `modified` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb3 COLLATE = utf8mb3_general_ci COMMENT = 'User Created Content (crests)' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of uccs
-- ----------------------------

-- ----------------------------
-- Table structure for updates
-- ----------------------------
DROP TABLE IF EXISTS `updates`;
CREATE TABLE `updates`  (
  `script_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `installed` tinyint NOT NULL DEFAULT 0,
  `install_date` datetime NOT NULL,
  `last_error` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  PRIMARY KEY (`script_name`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Table containing SQL update script information' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Records of updates
-- ----------------------------
INSERT INTO `updates` VALUES ('2019-02-04_aaemu_game.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2019-02-11_aaemu_game.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2019-02-12_aaemu_game.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2019-02-18_aaemu_game.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2019-02-27_aaemu_game.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2019-02-28_aaemu_game.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2019-03-06_aaemu_game.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2019-03-09_aaemu_game.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2019-03-13_aaemu_game.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2019-03-16_aaemu_game.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2019-03-20_aaemu_game.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2019-03-23_aaemu_game.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2019-05-07_aaemu_game.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2019-09-07_aaemu_game.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2020-04-29_aaemu_game_auction_house.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2020-04-30_aaemu_game.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2020-04-30_aaemu_game_items.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2020-05-09_aaemu_game_items.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2020-05-10_aaemu_game_mails.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2020-05-14_aaemu_game_items.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2020-05-16_aaemu_game_auction_hourse.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2020-05-25_aaemu_game_auction_house.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2020-12-04_aaemu_game_auction_house.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2020-12-09_aaemu_game_accounts.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2020-12-09_aaemu_game_items.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2020-12-10_aaemu_game_doodads.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2020-12-10_aaemu_game_hostile_faction_kills.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2020-12-10_aaemu_game_housing.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2020-12-26_aaemu_game_fix_world_id.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2021-04-10_aaemu_game_ucc.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2021-07-11_aaemu_game_transform.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2021-07-15_aaemu_game_furniture.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2021-07-21_aaemu_game_transform.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2021-09-11_aaemu_game_ucc.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2021-09-23_aaemu_game_music.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2021-11-12_aaemu_game_index_fix.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2022-02-10_aaemu_game_characters-return_district.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2022-02-18_aaemu_game_characters-create_at&update_at_fix.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2022-05-14_aaemu_game_item_containers.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2022-07-07_aaemu_game_coffers.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2022-07-08_aaemu_game_doodad_data.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2022-07-28_aaemu_game_expire_item.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2022-09-22_aaemu_game_mail_text.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2022-09-23_aaemu_game_characters_experience.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2023-10-08_aaemu_game_slaves.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2024-02-16_aaemu_game_ics.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2024-03-11_aaemu_game_item_containers.sql', 1, '2024-04-01 21:14:54', 'Initialized');
INSERT INTO `updates` VALUES ('2024-04-01_aaemu_game_ics.sql', 1, '2024-04-01 21:14:54', 'Initialized');

SET FOREIGN_KEY_CHECKS = 1;
