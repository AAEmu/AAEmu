/*
 Navicat Premium Data Transfer

 Source Server         : archeage
 Source Server Type    : MySQL
 Source Server Version : 80033
 Source Host           : localhost:3306
 Source Schema         : aaemu_game_5070

 Target Server Type    : MySQL
 Target Server Version : 80033
 File Encoding         : 65001

 Date: 16/09/2024 02:14:42
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
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Skillsets Exp' ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for accounts
-- ----------------------------
DROP TABLE IF EXISTS `accounts`;
CREATE TABLE `accounts`  (
  `account_id` bigint UNSIGNED NOT NULL,
  `access_level` int NOT NULL DEFAULT 0,
  `labor` int NOT NULL DEFAULT 0,
  `credits` int NOT NULL DEFAULT 0,
  `loyalty` int NOT NULL DEFAULT 0,
  `last_updated` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `last_login` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `last_labor_tick` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `last_credits_tick` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `last_loyalty_tick` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`account_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Account specific values not related to login' ROW_FORMAT = Dynamic;

-- ----------------------------
-- Triggers structure for table accounts
-- ----------------------------

DELIMITER //
CREATE TRIGGER update_timestamps
BEFORE UPDATE ON accounts
FOR EACH ROW
BEGIN
    SET NEW.last_updated = UTC_TIMESTAMP();
END;
//
DELIMITER ;

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
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Vocations' ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for appellations
-- ----------------------------
DROP TABLE IF EXISTS `appellations`;
CREATE TABLE `appellations`  (
  `id` int UNSIGNED NOT NULL,
  `active` tinyint(1) NOT NULL DEFAULT 0,
  `owner` int UNSIGNED NOT NULL,
  PRIMARY KEY (`id`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Earned titles' ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for attendances
-- ----------------------------
DROP TABLE IF EXISTS `attendances`;
CREATE TABLE `attendances`  (
  `id` tinyint UNSIGNED NOT NULL,
  `owner` bigint UNSIGNED NOT NULL,
  `account_attendance` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `accept` tinyint(1) NOT NULL,
  PRIMARY KEY (`id`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------------
-- Table structure for auction_house
-- ----------------------------------
DROP TABLE IF EXISTS `auction_house`;
CREATE TABLE `auction_house`  (
  `id` bigint NOT NULL AUTO_INCREMENT,
  `duration` tinyint NOT NULL,
  `item_id` bigint NOT NULL,
  `post_date` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Time when the auction item was put up for sale (in UTC)',
  `end_time` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Time when the sale period ends (in UTC)',
  `world_id` tinyint NOT NULL,
  `client_id` int NOT NULL,
  `client_name` varchar(45) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `start_money` int NOT NULL,
  `direct_money` int NOT NULL,
  `charge_percent` int NOT NULL,
  `bid_world_id` int NOT NULL,
  `bidder_id` int NOT NULL,
  `bidder_name` varchar(45) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `bid_money` int NOT NULL,
  `extra` int NOT NULL,
  `min_stack` int NOT NULL,
  `max_stack` int NOT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Listed AH Items' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for auction_solds_data
-- ----------------------------
DROP TABLE IF EXISTS `auction_solds_data`;
CREATE TABLE auction_solds_data (
    item_id INT UNSIGNED NOT NULL,
    item_grade TINYINT NOT NULL,
    date datetime NOT NULL,
    min_copper BIGINT NOT NULL,
    max_copper BIGINT NOT NULL,
    avg_copper BIGINT NOT NULL,
    volume INT NOT NULL,
    weekly_avg_copper BIGINT NOT NULL,
    PRIMARY KEY (item_id, item_grade, date)
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

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
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Sales history for the ICS' ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for blocked
-- ----------------------------
DROP TABLE IF EXISTS `blocked`;
CREATE TABLE `blocked`  (
  `owner` int NOT NULL,
  `blocked_id` int NOT NULL,
  PRIMARY KEY (`owner`, `blocked_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for characters
-- ----------------------------
DROP TABLE IF EXISTS `characters`;
CREATE TABLE `characters`  (
  `id` int UNSIGNED NOT NULL,
  `account_id` int UNSIGNED NOT NULL,
  `name` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `access_level` int UNSIGNED NOT NULL DEFAULT 0,
  `race` tinyint NOT NULL,
  `gender` tinyint(1) NOT NULL,
  `unit_model_params` blob NOT NULL,
  `level` tinyint NOT NULL,
  `experience` int NOT NULL,
  `recoverable_exp` int NOT NULL,
  `hp` int NOT NULL,
  `mp` int NOT NULL,
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
  `faction_name` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `expedition_id` int NOT NULL,
  `family` int UNSIGNED NOT NULL,
  `dead_count` mediumint UNSIGNED NOT NULL,
  `dead_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `rez_wait_duration` int NOT NULL,
  `rez_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `rez_penalty_duration` int NOT NULL,
  `leave_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `money` bigint NOT NULL DEFAULT 0,
  `money2` bigint NOT NULL DEFAULT 0,
  `honor_point` int NOT NULL DEFAULT 0,
  `vocation_point` int NOT NULL DEFAULT 0,
  `crime_point` int NOT NULL DEFAULT 0,
  `crime_record` int NOT NULL DEFAULT 0,
  `jury_point` int NOT NULL DEFAULT 0,
  `hostile_faction_kills` int NOT NULL DEFAULT 0,
  `pvp_honor` int NOT NULL DEFAULT 0,
  `delete_request_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `transfer_request_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `delete_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
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
  `online_time` int NOT NULL DEFAULT 0 COMMENT 'Time that the character has been online',
  PRIMARY KEY (`id`, `account_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Basic player character data' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for completed_quests
-- ----------------------------
DROP TABLE IF EXISTS `completed_quests`;
CREATE TABLE `completed_quests`  (
  `id` int UNSIGNED NOT NULL,
  `data` tinyblob NOT NULL,
  `owner` int UNSIGNED NOT NULL,
  PRIMARY KEY (`id`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Quests marked as completed for character' ROW_FORMAT = Dynamic;

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
  `farm_type` int NOT NULL DEFAULT 0 COMMENT 'farm type for Public Farm',
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Persistent doodads (e.g. tradepacks, furniture)' ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for expedition_applicants
-- ----------------------------
DROP TABLE IF EXISTS `expedition_applicants`;
CREATE TABLE `expedition_applicants`  (
  `expedition_id` int NOT NULL,
  `character_id` int NOT NULL,
  `character_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `character_level` tinyint(1) NOT NULL,
  `memo` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `reg_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  PRIMARY KEY (`expedition_id`, `character_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for expedition_members
-- ----------------------------
DROP TABLE IF EXISTS `expedition_members`;
CREATE TABLE `expedition_members`  (
  `character_id` int NOT NULL,
  `expedition_id` int NOT NULL,
  `name` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `level` tinyint UNSIGNED NOT NULL,
  `role` tinyint UNSIGNED NOT NULL,
  `last_leave_time` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `ability1` tinyint UNSIGNED NOT NULL,
  `ability2` tinyint UNSIGNED NOT NULL,
  `ability3` tinyint UNSIGNED NOT NULL,
  `memo` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  PRIMARY KEY (`character_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Guild members' ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for expedition_recruitments
-- ----------------------------
DROP TABLE IF EXISTS `expedition_recruitments`;
CREATE TABLE `expedition_recruitments`  (
  `expedition_id` int NOT NULL,
  `name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `level` int NULL DEFAULT NULL,
  `owner_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `introduce` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `reg_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `end_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `member_count` int NULL DEFAULT NULL,
  `interest` int NULL DEFAULT NULL,
  `apply` tinyint(1) NOT NULL,
  PRIMARY KEY (`expedition_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Guild recruitments' ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for expedition_role_policies
-- ----------------------------
DROP TABLE IF EXISTS `expedition_role_policies`;
CREATE TABLE `expedition_role_policies`  (
  `expedition_id` int NOT NULL,
  `role` tinyint UNSIGNED NOT NULL,
  `name` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
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
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Guild role settings' ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for expeditions
-- ----------------------------
DROP TABLE IF EXISTS `expeditions`;
CREATE TABLE `expeditions`  (
  `id` int NOT NULL,
  `owner` int NOT NULL DEFAULT 0,
  `owner_name` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `name` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `mother` int NOT NULL DEFAULT 0,
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `level` int NOT NULL DEFAULT 0,
  `exp` int NOT NULL DEFAULT 0,
  `protect_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `war_deposit` int NOT NULL DEFAULT 0,
  `daily_exp` int NOT NULL DEFAULT 0,
  `last_exp_update_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `is_level_update` tinyint(1) NOT NULL DEFAULT 0,
  `interest` int NOT NULL DEFAULT 0,
  `motd_title` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `motd_content` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `win` int NOT NULL DEFAULT 0,
  `lose` int NOT NULL DEFAULT 0,
  `draw` int NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Guilds' ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for family_members
-- ----------------------------
DROP TABLE IF EXISTS `family_members`;
CREATE TABLE `family_members`  (
  `character_id` int NOT NULL,
  `family_id` int NOT NULL,
  `name` varchar(45) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `role` tinyint(1) NOT NULL DEFAULT 0,
  `title` varchar(45) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL,
  PRIMARY KEY (`family_id`, `character_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Family members' ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for friends
-- ----------------------------
DROP TABLE IF EXISTS `friends`;
CREATE TABLE `friends`  (
  `id` int NOT NULL,
  `friend_id` int NOT NULL,
  `owner` int NOT NULL,
  PRIMARY KEY (`id`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Friendslist' ROW_FORMAT = Dynamic;

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
  `name` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
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
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Player buildings' ROW_FORMAT = Dynamic;

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
) ENGINE = InnoDB AUTO_INCREMENT = 100 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Contains what item will be displayed on which tab' ROW_FORMAT = Dynamic;

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
) ENGINE = InnoDB AUTO_INCREMENT = 2000000 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Possible Item listings that are for sale' ROW_FORMAT = Dynamic;

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
) ENGINE = InnoDB AUTO_INCREMENT = 1000000 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Has the actual sales items for the details' ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for item_containers
-- ----------------------------
DROP TABLE IF EXISTS `item_containers`;
CREATE TABLE `item_containers`  (
  `container_id` int UNSIGNED NOT NULL,
  `container_type` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL DEFAULT 'ItemContainer' COMMENT 'Partial Container Class Name',
  `slot_type` int NOT NULL DEFAULT 0 COMMENT 'Internal Container Type',
  `container_size` int NOT NULL DEFAULT 50 COMMENT 'Maximum Container Size',
  `owner_id` int UNSIGNED NOT NULL COMMENT 'Owning Character Id',
  `mate_id` int UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Owning Mate Id',
  PRIMARY KEY (`container_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for items
-- ----------------------------
DROP TABLE IF EXISTS `items`;
CREATE TABLE `items`  (
  `id` bigint UNSIGNED NOT NULL,
  `type` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `template_id` int UNSIGNED NOT NULL,
  `container_id` int UNSIGNED NOT NULL DEFAULT 0,
  `slot_type` int NOT NULL DEFAULT 0 COMMENT 'Internal Container Type',
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
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'All items' ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for mails
-- ----------------------------
DROP TABLE IF EXISTS `mails`;
CREATE TABLE `mails`  (
  `id` int NOT NULL,
  `type` int NOT NULL,
  `status` int NOT NULL,
  `title` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `text` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `sender_id` int NOT NULL DEFAULT 0,
  `sender_name` varchar(45) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `attachment_count` int NOT NULL DEFAULT 0,
  `receiver_id` int NOT NULL DEFAULT 0,
  `receiver_name` varchar(45) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
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
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'In-game mails' ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for mates
-- ----------------------------
DROP TABLE IF EXISTS `mates`;
CREATE TABLE `mates`  (
  `id` int UNSIGNED NOT NULL,
  `item_id` bigint UNSIGNED NOT NULL,
  `name` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `xp` int NOT NULL,
  `level` tinyint NOT NULL,
  `mileage` int NOT NULL,
  `hp` int NOT NULL,
  `mp` int NOT NULL,
  `owner` int UNSIGNED NOT NULL,
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`, `item_id`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Player mounts and pets' ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for music
-- ----------------------------
DROP TABLE IF EXISTS `music`;
CREATE TABLE `music`  (
  `id` int NOT NULL AUTO_INCREMENT,
  `author` int NOT NULL COMMENT 'PlayerId',
  `title` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `song` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL COMMENT 'Song MML',
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'User Created Content (music)' ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for options
-- ----------------------------
DROP TABLE IF EXISTS `options`;
CREATE TABLE `options`  (
  `key` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `value` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `owner` int UNSIGNED NOT NULL,
  PRIMARY KEY (`key`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Settings that the client stores on the server' ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for portal_book_coords
-- ----------------------------
DROP TABLE IF EXISTS `portal_book_coords`;
CREATE TABLE `portal_book_coords`  (
  `id` int NOT NULL,
  `name` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `x` int NULL DEFAULT 0,
  `y` int NULL DEFAULT 0,
  `z` int NULL DEFAULT 0,
  `zone_id` int NULL DEFAULT 0,
  `z_rot` int NULL DEFAULT 0,
  `sub_zone_id` int NULL DEFAULT 0,
  `owner` int NOT NULL,
  PRIMARY KEY (`id`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Recorded house portals in the portal book' ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for portal_visited_district
-- ----------------------------
DROP TABLE IF EXISTS `portal_visited_district`;
CREATE TABLE `portal_visited_district`  (
  `id` int NOT NULL,
  `subzone` int NOT NULL,
  `owner` int NOT NULL,
  PRIMARY KEY (`id`, `subzone`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'List of visited area for the portal book' ROW_FORMAT = Dynamic;

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
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Currently open quests' ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for resident_members
-- ----------------------------
DROP TABLE IF EXISTS `resident_members`;
CREATE TABLE `resident_members`  (
  `id` int NOT NULL,
  `resident_id` int NOT NULL,
  `name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `level` tinyint(1) NOT NULL,
  `family` int NULL DEFAULT NULL,
  `service_point` int NULL DEFAULT NULL,
  PRIMARY KEY (`id`, `resident_id`) USING BTREE
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = DYNAMIC;

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
) ENGINE = InnoDB AUTO_INCREMENT = 30 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci ROW_FORMAT = DYNAMIC;

-- ----------------------------
-- Table structure for skills
-- ----------------------------
DROP TABLE IF EXISTS `skills`;
CREATE TABLE `skills`  (
  `id` int UNSIGNED NOT NULL,
  `level` tinyint NOT NULL,
  `type` enum('Skill','Buff') CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NOT NULL,
  `owner` int UNSIGNED NOT NULL,
  PRIMARY KEY (`id`, `owner`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Learned character skills' ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for slaves
-- ----------------------------
DROP TABLE IF EXISTS `slaves`;
CREATE TABLE `slaves`  (
  `id` int UNSIGNED NOT NULL,
  `item_id` int UNSIGNED NULL DEFAULT NULL COMMENT 'Item that is used to summon this vehicle',
  `template_id` int UNSIGNED NULL DEFAULT NULL COMMENT 'Slave template Id of this vehicle',
  `attach_point` int NULL DEFAULT NULL COMMENT 'Binding point Id',
  `name` text CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL,
  `owner_type` int UNSIGNED NULL DEFAULT 0 COMMENT 'Parent unit type',
  `owner_id` int UNSIGNED NULL DEFAULT 0 COMMENT 'Parent unit DB Id',
  `summoner` int UNSIGNED NULL DEFAULT NULL COMMENT 'Owning player',
  `created_at` datetime NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` datetime NULL DEFAULT CURRENT_TIMESTAMP,
  `hp` int NULL DEFAULT NULL,
  `mp` int NULL DEFAULT NULL,
  `x` float NULL DEFAULT NULL,
  `y` float NULL DEFAULT NULL,
  `z` float NULL DEFAULT NULL,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'Player vehicles summons' ROW_FORMAT = Dynamic;

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
) ENGINE = InnoDB AUTO_INCREMENT = 1 CHARACTER SET = utf8mb4 COLLATE = utf8mb4_general_ci COMMENT = 'User Created Content (crests)' ROW_FORMAT = Dynamic;

-- ----------------------------
-- Table structure for divine_clock
-- ----------------------------
DROP TABLE IF EXISTS `divine_clock`;
CREATE TABLE `divine_clock`  (
  `id` int NOT NULL AUTO_INCREMENT,
  `account_id` bigint UNSIGNED NOT NULL,
  `schedule_item_id` INT UNSIGNED NOT NULL COMMENT 'This field corresponds to the id field in the schedule_items table',
  `gave` tinyint NOT NULL DEFAULT 0 COMMENT 'Number of clicks taken today',
  `cumulated` int UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Time that has been passed already',
  PRIMARY KEY (`id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci;

SET FOREIGN_KEY_CHECKS = 1;
