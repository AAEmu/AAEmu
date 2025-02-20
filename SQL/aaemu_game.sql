CREATE DATABASE IF NOT EXISTS `aaemu_game`;
USE aaemu_game;
-- ----------------------------------------------------------------------------------------------
-- Make sure to remove the above two lines if you want use your own DB/Schema names during import
-- ----------------------------------------------------------------------------------------------

SET NAMES utf8;
SET time_zone = '+00:00';
SET foreign_key_checks = 0;

DROP TABLE IF EXISTS `abilities`;
CREATE TABLE `abilities` (
  `id` tinyint unsigned NOT NULL,
  `exp` int NOT NULL,
  `owner` int unsigned NOT NULL,
  PRIMARY KEY (`id`,`owner`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Skillsets Exp';


DROP TABLE IF EXISTS `accounts`;
CREATE TABLE `accounts` (
  `account_id` INT(11) NOT NULL,
  `access_level` INT(11) NOT NULL DEFAULT '0',
  `labor` INT(11) NOT NULL DEFAULT '0',
  `credits` INT(11) NOT NULL DEFAULT '0',
  `loyalty` INT(11) NOT NULL DEFAULT '0',
  `last_updated` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `last_login` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `last_labor_tick` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `last_credits_tick` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `last_loyalty_tick` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `divine_clock_time` INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Time that has been passed already',
  `divine_clock_taken` INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Number of clicks taken today',
  PRIMARY KEY (`account_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Account specific values not related to login';

DELIMITER //
CREATE TRIGGER update_timestamps BEFORE UPDATE ON accounts
FOR EACH ROW
BEGIN
   SET NEW.last_updated = UTC_TIMESTAMP();
END;//
DELIMITER ;

DROP TABLE IF EXISTS `actabilities`;
CREATE TABLE `actabilities` (
  `id` int unsigned NOT NULL,
  `point` int unsigned NOT NULL DEFAULT '0',
  `step` tinyint unsigned NOT NULL DEFAULT '0',
  `owner` int unsigned NOT NULL,
  PRIMARY KEY (`owner`,`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Vocations';


DROP TABLE IF EXISTS `appellations`;
CREATE TABLE `appellations` (
  `id` int unsigned NOT NULL,
  `active` tinyint(1) NOT NULL DEFAULT '0',
  `owner` int unsigned NOT NULL,
  PRIMARY KEY (`id`,`owner`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Earned titles';


-- ----------------------------------
-- Table structure for auction_house
-- ----------------------------------
DROP TABLE IF EXISTS `auction_house`;
CREATE TABLE `auction_house` (
	`id` BIGINT(20) NOT NULL AUTO_INCREMENT,
	`duration` TINYINT(4) NOT NULL,
	`item_id` BIGINT(20) NOT NULL,
	`post_date` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Time when the auction item was put up for sale (in UTC)',
	`stack_size` INT(11) NOT NULL,
	`end_time` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Time when the sale period ends (in UTC)',
	`world_id` TINYINT(4) NOT NULL,
	`client_id` INT(11) NOT NULL,
	`client_name` VARCHAR(45) NOT NULL COLLATE 'utf8mb4_general_ci',
	`start_money` INT(11) NOT NULL,
	`direct_money` INT(11) NOT NULL,
	`bid_world_id` INT(11) NOT NULL,
	`bidder_id` INT(11) NOT NULL,
	`bidder_name` VARCHAR(45) NOT NULL COLLATE 'utf8mb4_general_ci',
	`bid_money` INT(11) NOT NULL,
	`extra` INT(11) NOT NULL,
	PRIMARY KEY (`id`) USING BTREE
)
COMMENT='Listed AH Items'
COLLATE='utf8mb4_general_ci'
ENGINE=InnoDB
ROW_FORMAT=DYNAMIC
;


DROP TABLE IF EXISTS `blocked`;
CREATE TABLE `blocked` (
  `owner` int NOT NULL,
  `blocked_id` int NOT NULL,
  PRIMARY KEY (`owner`,`blocked_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;


DROP TABLE IF EXISTS `characters`;
CREATE TABLE `characters` (
  `id` int unsigned NOT NULL,
  `account_id` int unsigned NOT NULL,
  `name` varchar(128) NOT NULL,
  `access_level` int unsigned NOT NULL DEFAULT '0',
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
  `world_id` int unsigned NOT NULL,
  `zone_id` int unsigned NOT NULL,
  `x` float NOT NULL,
  `y` float NOT NULL,
  `z` float NOT NULL,
  `yaw` float NOT NULL DEFAULT '0',
  `pitch` float NOT NULL DEFAULT '0',
  `roll` float NOT NULL DEFAULT '0',
  `faction_id` int unsigned NOT NULL,
  `faction_name` varchar(128) NOT NULL,
  `expedition_id` int NOT NULL,
  `family` int unsigned NOT NULL,
  `dead_count` mediumint unsigned NOT NULL,
  `dead_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `rez_wait_duration` int NOT NULL,
  `rez_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `rez_penalty_duration` int NOT NULL,
  `leave_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `money` bigint NOT NULL DEFAULT '0',
  `money2` bigint NOT NULL DEFAULT '0',
  `honor_point` int NOT NULL DEFAULT '0',
  `vocation_point` int NOT NULL DEFAULT '0',
  `crime_point` int NOT NULL DEFAULT '0',
  `crime_record` int NOT NULL DEFAULT '0',
  `jury_point` int NOT NULL DEFAULT '0',
  `hostile_faction_kills` int NOT NULL DEFAULT '0',
  `pvp_honor` int NOT NULL DEFAULT '0',
  `delete_request_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `transfer_request_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `delete_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `auto_use_aapoint` tinyint(1) NOT NULL,
  `prev_point` int NOT NULL,
  `point` int NOT NULL,
  `gift` int NOT NULL,
  `num_inv_slot` tinyint unsigned NOT NULL DEFAULT '50',
  `num_bank_slot` smallint unsigned NOT NULL DEFAULT '50',
  `expanded_expert` tinyint NOT NULL,
  `slots` blob NOT NULL,
  `created_at` datetime(0) NOT NULL DEFAULT CURRENT_TIMESTAMP(0),
  `updated_at` datetime(0) NOT NULL DEFAULT '0001-01-01 00:00:00',
  `deleted` int(11) NOT NULL DEFAULT 0,
  `return_district` int(11) NOT NULL DEFAULT 0,
  `online_time` INT(11) NOT NULL DEFAULT 0 COMMENT 'Time that the character has been online',
  PRIMARY KEY (`id`, `account_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci COMMENT = 'Basic player character data' ROW_FORMAT = DYNAMIC;


DROP TABLE IF EXISTS `completed_quests`;
CREATE TABLE `completed_quests` (
  `id` int unsigned NOT NULL,
  `data` tinyblob NOT NULL,
  `owner` int unsigned NOT NULL,
  PRIMARY KEY (`id`,`owner`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Quests marked as completed for character';


DROP TABLE IF EXISTS `doodads`;
CREATE TABLE `doodads` (
  `id` int unsigned NOT NULL AUTO_INCREMENT,
  `owner_id` int DEFAULT NULL COMMENT 'Character DB Id',
  `owner_type` tinyint unsigned DEFAULT '255',
  `attach_point` int unsigned NULL DEFAULT '0' COMMENT 'Slot this doodad fits in on the owner',
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
  `scale` FLOAT NOT NULL DEFAULT '1' ,
  `item_id` bigint unsigned NOT NULL DEFAULT '0' COMMENT 'Item DB Id of the associated item',
  `house_id` int unsigned NOT NULL DEFAULT '0' COMMENT 'House DB Id if it is on actual house land',
  `parent_doodad` int unsigned NOT NULL DEFAULT '0' COMMENT 'doodads DB Id this object is standing on',
  `item_template_id` int unsigned NOT NULL DEFAULT '0' COMMENT 'ItemTemplateId of associated item',
  `item_container_id` int unsigned NOT NULL DEFAULT '0' COMMENT 'ItemContainer Id for Coffers',
  `data` int NOT NULL DEFAULT '0' COMMENT 'Doodad specific data',
  `farm_type` int NOT NULL DEFAULT '0' COMMENT 'farm type for Public Farm',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Persistent doodads (e.g. tradepacks, furniture)';


DROP TABLE IF EXISTS `expedition_members`;
CREATE TABLE `expedition_members` (
  `character_id` int NOT NULL,
  `expedition_id` int NOT NULL,
  `name` varchar(128) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `level` tinyint unsigned NOT NULL,
  `role` tinyint unsigned NOT NULL,
  `last_leave_time` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `ability1` tinyint unsigned NOT NULL,
  `ability2` tinyint unsigned NOT NULL,
  `ability3` tinyint unsigned NOT NULL,
  `memo` varchar(128) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  PRIMARY KEY (`character_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Guild members';


DROP TABLE IF EXISTS `expedition_role_policies`;
CREATE TABLE `expedition_role_policies` (
  `expedition_id` int NOT NULL,
  `role` tinyint unsigned NOT NULL,
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
  PRIMARY KEY (`expedition_id`,`role`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Guild role settings';


DROP TABLE IF EXISTS `expeditions`;
CREATE TABLE `expeditions` (
  `id` int NOT NULL,
  `owner` int NOT NULL,
  `owner_name` varchar(128) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `name` varchar(128) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `mother` int NOT NULL,
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Guilds';


DROP TABLE IF EXISTS `family_members`;
CREATE TABLE `family_members` (
  `character_id` int NOT NULL,
  `family_id` int NOT NULL,
  `name` varchar(45) NOT NULL,
  `role` tinyint(1) NOT NULL DEFAULT '0',
  `title` varchar(45) DEFAULT NULL,
  PRIMARY KEY (`family_id`,`character_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Family members';


DROP TABLE IF EXISTS `friends`;
CREATE TABLE `friends` (
  `id` int NOT NULL,
  `friend_id` int NOT NULL,
  `owner` int NOT NULL,
  PRIMARY KEY (`id`,`owner`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Friendslist';


DROP TABLE IF EXISTS `housings`;
CREATE TABLE `housings` (
  `id` int NOT NULL,
  `account_id` int unsigned NOT NULL,
  `owner` int unsigned NOT NULL,
  `co_owner` int unsigned NOT NULL,
  `template_id` int unsigned NOT NULL,
  `name` varchar(128) NOT NULL,
  `x` float NOT NULL,
  `y` float NOT NULL,
  `z` float NOT NULL,
  `yaw` float NOT NULL DEFAULT '0',
  `pitch` float NOT NULL DEFAULT '0',
  `roll` float NOT NULL DEFAULT '0',
  `current_step` tinyint NOT NULL,
  `current_action` int NOT NULL DEFAULT '0',
  `permission` tinyint NOT NULL,
  `place_date` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `protected_until` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `faction_id` int unsigned NOT NULL DEFAULT '1',
  `sell_to` int unsigned NOT NULL DEFAULT '0',
  `sell_price` bigint NOT NULL DEFAULT '0',
  `allow_recover` tinyint unsigned NOT NULL DEFAULT '1',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Player buildings';

-- ----------------------------
-- Records of housings
-- ----------------------------
INSERT INTO `housings` VALUES (1, 0, 0, 0, 139, 'Archeum Lodestone', 19643., 24385.4, 168.9, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', '2043-03-03 00:00:00', 2, 0, 0, 0);
INSERT INTO `housings` VALUES (2, 0, 0, 0, 184, 'Archeum Lodestone', 19952.6, 24275.5, 140.4, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', '2043-03-03 00:00:00', 2, 0, 0, 0);
INSERT INTO `housings` VALUES (3, 0, 0, 0, 185, 'Archeum Lodestone', 20379.4, 24126.2, 123.6, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', '2043-03-03 00:00:00', 2, 0, 0, 0);
INSERT INTO `housings` VALUES (4, 0, 0, 0, 186, 'Archeum Lodestone', 21235.7, 23918.5, 165.0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', '2043-03-03 00:00:00', 2, 0, 0, 0);
INSERT INTO `housings` VALUES (5, 0, 0, 0, 187, 'Archeum Lodestone', 21441.7, 24211.7, 154.7, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', '2043-03-03 00:00:00', 2, 0, 0, 0);
INSERT INTO `housings` VALUES (6, 0, 0, 0, 188, 'Archeum Lodestone', 22048.2, 24241.1, 154.8, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', '2043-03-03 00:00:00', 2, 0, 0, 0);
INSERT INTO `housings` VALUES (7, 0, 0, 0, 189, 'Archeum Lodestone', 19644.0, 25077.6, 164.6, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', '2043-03-03 00:00:00', 2, 0, 0, 0);
INSERT INTO `housings` VALUES (8, 0, 0, 0, 190, 'Archeum Lodestone', 20325.6, 25174.6, 172.9, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', '2043-03-03 00:00:00', 2, 0, 0, 0);
INSERT INTO `housings` VALUES (9, 0, 0, 0, 191, 'Archeum Lodestone', 20890.8, 25238.5, 193.7, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', '2043-03-03 00:00:00', 2, 0, 0, 0);
INSERT INTO `housings` VALUES (10, 0, 0, 0, 192, 'Archeum Lodestone', 21956, 24881.7, 206.3, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', '2043-03-03 00:00:00', 2, 0, 0, 0);
INSERT INTO `housings` VALUES (11, 0, 0, 0, 271, 'Archeum Lodestone', 23060.8, 25148.3, 142.0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', '2043-03-03 00:00:00', 2, 0, 0, 0);
INSERT INTO `housings` VALUES (12, 0, 0, 0, 272, 'Archeum Lodestone', 21800.3, 26893.9, 137.7, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', '2043-03-03 00:00:00', 2, 0, 0, 0);

DROP TABLE IF EXISTS `items`;
CREATE TABLE `items` (
  `id` bigint unsigned NOT NULL,
  `type` varchar(100) NOT NULL,
  `template_id` int unsigned NOT NULL,
  `container_id` int unsigned NOT NULL DEFAULT '0',
  `slot_type` int NOT NULL DEFAULT 0 COMMENT 'Internal Container Type',
  `slot` int NOT NULL,
  `count` int NOT NULL,
  `details` blob,
  `lifespan_mins` int NOT NULL,
  `made_unit_id` int unsigned NOT NULL DEFAULT '0',
  `unsecure_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `unpack_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `owner` int unsigned NOT NULL,
  `grade` tinyint(1) DEFAULT '0',
  `flags` tinyint unsigned NOT NULL,
  `created_at` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `ucc` int unsigned NOT NULL DEFAULT '0',
  `expire_time` DATETIME NULL DEFAULT NULL COMMENT 'Fixed time expire', 
  `expire_online_minutes` DOUBLE NOT NULL DEFAULT '0' COMMENT 'Time left when player online',
  `charge_time` DATETIME NULL DEFAULT NULL COMMENT 'Time charged items got activated',
  `charge_count` INT NOT NULL DEFAULT '0' COMMENT 'Number of charges left',
  PRIMARY KEY (`id`) USING BTREE,
  KEY `owner` (`owner`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='All items';


DROP TABLE IF EXISTS `mails`;
CREATE TABLE `mails` (
  `id` int NOT NULL,
  `type` int NOT NULL,
  `status` int NOT NULL,
  `title` TEXT CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `text` TEXT CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `sender_id` int NOT NULL DEFAULT '0',
  `sender_name` varchar(45) NOT NULL,
  `attachment_count` int NOT NULL DEFAULT '0',
  `receiver_id` int NOT NULL DEFAULT '0',
  `receiver_name` varchar(45) NOT NULL,
  `open_date` datetime NOT NULL,
  `send_date` datetime NOT NULL,
  `received_date` datetime NOT NULL,
  `returned` int NOT NULL,
  `extra` bigint NOT NULL,
  `money_amount_1` int NOT NULL,
  `money_amount_2` int NOT NULL,
  `money_amount_3` int NOT NULL,
  `attachment0` bigint NOT NULL DEFAULT '0',
  `attachment1` bigint NOT NULL DEFAULT '0',
  `attachment2` bigint NOT NULL DEFAULT '0',
  `attachment3` bigint NOT NULL DEFAULT '0',
  `attachment4` bigint NOT NULL DEFAULT '0',
  `attachment5` bigint NOT NULL DEFAULT '0',
  `attachment6` bigint NOT NULL DEFAULT '0',
  `attachment7` bigint NOT NULL DEFAULT '0',
  `attachment8` bigint NOT NULL DEFAULT '0',
  `attachment9` bigint NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='In-game mails';


DROP TABLE IF EXISTS `mates`;
CREATE TABLE `mates` (
  `id` int unsigned NOT NULL,
  `item_id` bigint unsigned NOT NULL,
  `name` text CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `xp` int NOT NULL,
  `level` tinyint NOT NULL,
  `mileage` int NOT NULL,
  `hp` int NOT NULL,
  `mp` int NOT NULL,
  `owner` int unsigned NOT NULL,
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`,`item_id`,`owner`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Player mounts and pets';


DROP TABLE IF EXISTS `options`;
CREATE TABLE `options` (
  `key` varchar(100) NOT NULL,
  `value` text NOT NULL,
  `owner` int unsigned NOT NULL,
  PRIMARY KEY (`key`,`owner`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Settings that the client stores on the server';


DROP TABLE IF EXISTS `portal_book_coords`;
CREATE TABLE `portal_book_coords` (
  `id` int NOT NULL,
  `name` varchar(128) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `x` int DEFAULT '0',
  `y` int DEFAULT '0',
  `z` int DEFAULT '0',
  `zone_id` int DEFAULT '0',
  `z_rot` int DEFAULT '0',
  `sub_zone_id` int DEFAULT '0',
  `owner` int NOT NULL,
  PRIMARY KEY (`id`,`owner`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Recorded house portals in the portal book';


DROP TABLE IF EXISTS `portal_visited_district`;
CREATE TABLE `portal_visited_district` (
  `id` int NOT NULL,
  `subzone` int NOT NULL,
  `owner` int NOT NULL,
  PRIMARY KEY (`id`,`subzone`,`owner`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='List of visited area for the portal book';


DROP TABLE IF EXISTS `quests`;
CREATE TABLE `quests` (
  `id` int unsigned NOT NULL,
  `template_id` int unsigned NOT NULL,
  `data` tinyblob NOT NULL,
  `status` tinyint NOT NULL,
  `owner` int unsigned NOT NULL,
  PRIMARY KEY (`id`,`owner`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Currently open quests';


DROP TABLE IF EXISTS `skills`;
CREATE TABLE `skills` (
  `id` int unsigned NOT NULL,
  `level` tinyint NOT NULL,
  `type` enum('Skill','Buff') NOT NULL,
  `owner` int unsigned NOT NULL,
  PRIMARY KEY (`id`,`owner`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Learned character skills';


DROP TABLE IF EXISTS `uccs`;
CREATE TABLE `uccs` (
  `id` int NOT NULL AUTO_INCREMENT,
  `uploader_id` int NOT NULL COMMENT 'PlayerID',
  `type` tinyint NOT NULL,
  `data` mediumblob COMMENT 'Raw uploaded UCC data',
  `pattern1` int unsigned NOT NULL COMMENT 'Background pattern',
  `pattern2` int unsigned NOT NULL COMMENT 'Crest',
  `color1R` int unsigned NOT NULL,
  `color1G` int unsigned NOT NULL,
  `color1B` int unsigned NOT NULL,
  `color2R` int unsigned NOT NULL,
  `color2G` int unsigned NOT NULL,
  `color2B` int unsigned NOT NULL,
  `color3R` int unsigned NOT NULL,
  `color3G` int unsigned NOT NULL,
  `color3B` int unsigned NOT NULL,
  `modified` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='User Created Content (crests)';


DROP TABLE IF EXISTS `music`;
CREATE TABLE `music` (
  `id` int NOT NULL AUTO_INCREMENT,
  `author` int NOT NULL COMMENT 'PlayerId',
  `title` varchar(128) NOT NULL,
  `song` text NOT NULL COMMENT 'Song MML',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='User Created Content (music)';


DROP TABLE IF EXISTS `item_containers`;
CREATE TABLE `item_containers` (
  `container_id` int unsigned NOT NULL,
  `container_type` varchar(64) COLLATE 'utf8mb4_general_ci' NOT NULL DEFAULT 'ItemContainer' COMMENT 'Partial Container Class Name',
  `slot_type` int NOT NULL DEFAULT 0 COMMENT 'Internal Container Type',
  `container_size` int NOT NULL DEFAULT '50' COMMENT 'Maximum Container Size',
  `owner_id` int unsigned NOT NULL COMMENT 'Owning Character Id',
  `mate_id` int unsigned NOT NULL DEFAULT '0' COMMENT 'Owning Mate Id', 
  PRIMARY KEY (`container_id`) 
) COLLATE 'utf8mb4_general_ci';


DROP TABLE IF EXISTS `slaves`;
CREATE TABLE `slaves` (
	`id` INT(10) UNSIGNED NOT NULL,
	`item_id` INT(10) UNSIGNED NULL DEFAULT NULL COMMENT 'Item that is used to summon this vehicle',
	`template_id` INT(10) UNSIGNED NULL DEFAULT NULL COMMENT 'Slave template Id of this vehicle',
	`attach_point` INT(10) NULL DEFAULT NULL COMMENT 'Binding point Id',
	`name` TEXT NULL DEFAULT NULL COLLATE 'utf8mb4_general_ci',
	`owner_type` INT(10) UNSIGNED NULL DEFAULT '0' COMMENT 'Parent unit type',
	`owner_id` INT(10) UNSIGNED NULL DEFAULT '0' COMMENT 'Parent unit DB Id',
	`summoner` INT(10) UNSIGNED NULL DEFAULT NULL COMMENT 'Owning player',
	`created_at` DATETIME NULL DEFAULT CURRENT_TIMESTAMP,
	`updated_at` DATETIME NULL DEFAULT CURRENT_TIMESTAMP,
	`hp` INT(11) NULL DEFAULT NULL,
	`mp` INT(11) NULL DEFAULT NULL,
	`x` FLOAT NULL DEFAULT NULL,
	`y` FLOAT NULL DEFAULT NULL,
	`z` FLOAT NULL DEFAULT NULL,
	PRIMARY KEY (`id`) USING BTREE
) COMMENT='Player vehicles summons' COLLATE 'utf8mb4_general_ci' ENGINE=InnoDB;


DROP TABLE IF EXISTS `ics_skus`;
CREATE TABLE `ics_skus` (
    `sku` INT(10) UNSIGNED NOT NULL AUTO_INCREMENT,
    `shop_id` INT(10) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Reference to the shop item',
    `position` INT(10) NOT NULL DEFAULT '0' COMMENT 'Used for display order inside the item details',
    `item_id` INT(10) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Item that is for sale',
    `item_count` INT(10) UNSIGNED NOT NULL DEFAULT '1' COMMENT 'Number of items for this detail',
    `select_type` TINYINT(3) UNSIGNED NOT NULL DEFAULT '0',
    `is_default` TINYINT(3) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Is this the default selection?',
    `event_type` TINYINT(3) UNSIGNED NOT NULL DEFAULT '0',
    `event_end_date` DATETIME NULL DEFAULT NULL,
    `currency` TINYINT(3) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Credits(0), AAPoints(1), Loyalty(2), Coins(3)',
    `price` INT(10) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Price of the item',
    `discount_price` INT(10) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Discounted price (this is used if set)',
    `bonus_item_id` INT(10) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Bonus item included for this purchase',
    `bonus_item_count` INT(10) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Amount of bonus items included',
    PRIMARY KEY (`sku`) USING BTREE
)
COMMENT='Has the actual sales items for the details'
COLLATE='utf8mb4_general_ci'
ENGINE=InnoDB
AUTO_INCREMENT=1000000
;


DROP TABLE IF EXISTS `ics_shop_items`;
CREATE TABLE `ics_shop_items` (
    `shop_id` INT(10) UNSIGNED NOT NULL AUTO_INCREMENT COMMENT 'SKU item id',
    `display_item_id` INT(10) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Item who\'s icon to use for displaying in the shop, leave 0 for first item in the group',
    `name` TEXT NULL DEFAULT NULL COMMENT 'Can be used to override the name in the shop' COLLATE 'utf8mb4_general_ci',
    `limited_type` TINYINT(3) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Enables limited stock mode if non-zero, Account(1), Chracter(2)',
    `limited_stock_max` INT(10) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Number of items left in stock for this SKU if limited stock is enabled',
    `level_min` TINYINT(3) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Minimum level to buy the item (does not show on UI)',
    `level_max` TINYINT(3) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Maximum level to buy the item (does not show on UI)',
    `buy_restrict_type` TINYINT(3) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Buy restriction rule, none (0), level (1) or quest(2)',
    `buy_restrict_id` INT(10) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Level or QuestId for restrict rule',
    `is_sale` TINYINT(3) UNSIGNED NOT NULL DEFAULT '0',
    `is_hidden` TINYINT(3) UNSIGNED NOT NULL DEFAULT '0',
    `sale_start` DATETIME NULL DEFAULT NULL COMMENT 'Limited sale start time',
    `sale_end` DATETIME NULL DEFAULT NULL COMMENT 'Limited sale end time',
    `shop_buttons` TINYINT(3) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'All (0), NoCart (1), NoGift (2), OnlyBuy (3)',
    `remaining` INT(11) NOT NULL DEFAULT '-1' COMMENT 'Number of items remaining, only for tab 1-1 (limited)',
    PRIMARY KEY (`shop_id`) USING BTREE
)
COMMENT='Possible Item listings that are for sale'
COLLATE='utf8mb4_general_ci'
ENGINE=InnoDB
AUTO_INCREMENT=2000000
;


DROP TABLE IF EXISTS `ics_menu`;
CREATE TABLE `ics_menu` (
    `id` INT(11) NOT NULL AUTO_INCREMENT,
    `main_tab` TINYINT(3) UNSIGNED NOT NULL DEFAULT '1' COMMENT 'Which main tab to display on',
    `sub_tab` TINYINT(3) UNSIGNED NOT NULL DEFAULT '1' COMMENT 'Which sub tab to display on',
    `tab_pos` INT(11) NOT NULL DEFAULT '0' COMMENT 'Used to change display order',
    `shop_id` INT(11) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Id of the item group for sale (shop item)',
    PRIMARY KEY (`id`) USING BTREE
)
COMMENT='Contains what item will be displayed on which tab'
COLLATE='utf8mb4_general_ci'
ENGINE=InnoDB
AUTO_INCREMENT=100
;


DROP TABLE IF EXISTS `audit_ics_sales`;
CREATE TABLE `audit_ics_sales` (
    `id` BIGINT(20) UNSIGNED NOT NULL AUTO_INCREMENT,
    `buyer_account` INT(10) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Account ID of the person buying this item',
    `buyer_char` INT(10) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Character that was logged in when buying',
    `target_account` INT(10) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Account of the person receiving the goods',
    `target_char` INT(10) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Character that received the goods',
    `sale_date` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Time of purchase (in UTC)',
    `shop_item_id` INT(11) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Shop item entry id of the sold item',
    `sku` INT(11) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'SKU of the sold item',
    `sale_cost` INT(11) NOT NULL DEFAULT '0' COMMENT 'Amount this item was sold for',
    `sale_currency` TINYINT(4) UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Which currency was used',
    `description` TEXT NOT NULL COMMENT 'Added description of this transaction' COLLATE 'utf8mb4_general_ci',
    PRIMARY KEY (`id`) USING BTREE,
    INDEX `buyer_account` (`buyer_account`) USING BTREE,
    INDEX `buyer_char` (`buyer_char`) USING BTREE,
    INDEX `target_account` (`target_account`) USING BTREE,
    INDEX `target_char` (`target_char`) USING BTREE
)
COMMENT='Sales history for the ICS'
COLLATE='utf8mb4_general_ci'
ENGINE=InnoDB
;


CREATE TABLE `audit_char_sus` (
	`id` BIGINT(20) UNSIGNED NOT NULL AUTO_INCREMENT,
	`sus_date` DATETIME NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Time of incident',
	`sus_category` VARCHAR(64) NULL DEFAULT 'None' COMMENT 'Category name for the activity' COLLATE 'utf8mb4_general_ci',
	`sus_account` INT(10) UNSIGNED NULL DEFAULT '0' COMMENT 'Involved account Id (if any)',
	`sus_character` INT(10) UNSIGNED NULL DEFAULT '0' COMMENT 'Involved character Id (if any)',
	`zone_group` INT UNSIGNED NULL DEFAULT '0',
	`x` FLOAT NULL DEFAULT '0',
	`y` FLOAT NULL DEFAULT '0',
	`z` FLOAT NULL DEFAULT '0',
	`description` TEXT NULL COMMENT 'Description of the incident' COLLATE 'utf8mb4_general_ci',
	PRIMARY KEY (`id`) USING BTREE,
	INDEX `sus_date` (`sus_date`),
	INDEX `sus_account` (`sus_account`),
	INDEX `sus_character` (`sus_character`),
	INDEX `sus_category` (`sus_category`)
)
COLLATE='utf8mb4_general_ci'
ENGINE=InnoDB
;
