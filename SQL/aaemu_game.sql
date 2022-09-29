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
  `account_id` int NOT NULL,
  `credits` int NOT NULL DEFAULT '0',
  PRIMARY KEY (`account_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Account specific values not related to login';


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


DROP TABLE IF EXISTS `auction_house`;
CREATE TABLE `auction_house` (
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
  `unsecure_date_time` varchar(45) NOT NULL,
  `unpack_date_time` varchar(45) NOT NULL,
  `world_id_2` tinyint NOT NULL,
  `client_id` int NOT NULL,
  `client_name` varchar(45) NOT NULL,
  `start_money` int NOT NULL,
  `direct_money` int NOT NULL,
  `bid_world_id` tinyint(1) NOT NULL,
  `bidder_id` int NOT NULL,
  `bidder_name` varchar(45) NOT NULL,
  `bid_money` int NOT NULL,
  `extra` int NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Listed AH Items';


DROP TABLE IF EXISTS `blocked`;
CREATE TABLE `blocked` (
  `owner` int NOT NULL,
  `blocked_id` int NOT NULL,
  PRIMARY KEY (`owner`,`blocked_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;


DROP TABLE IF EXISTS `cash_shop_item`;
CREATE TABLE `cash_shop_item` (
  `id` int unsigned NOT NULL AUTO_INCREMENT COMMENT 'shop_id',
  `uniq_id` int unsigned DEFAULT '0' COMMENT 'Unique ID',
  `cash_name` varchar(255) NOT NULL COMMENT 'Sale Item Name',
  `main_tab` tinyint unsigned DEFAULT '1' COMMENT 'Main Tab Page 1-6',
  `sub_tab` tinyint unsigned DEFAULT '1' COMMENT 'Sub Tab Page 1-7',
  `level_min` tinyint unsigned DEFAULT '0' COMMENT 'Minimum level to buy',
  `level_max` tinyint unsigned DEFAULT '0' COMMENT 'Maximum level to buy',
  `item_template_id` int unsigned DEFAULT '0' COMMENT 'Item Template Id',
  `is_sell` tinyint unsigned DEFAULT '0' COMMENT 'Is it for sale',
  `is_hidden` tinyint unsigned DEFAULT '0' COMMENT 'Hidden item',
  `limit_type` tinyint unsigned DEFAULT '0',
  `buy_count` smallint unsigned DEFAULT '0',
  `buy_type` tinyint unsigned DEFAULT '0',
  `buy_id` int unsigned DEFAULT '0',
  `start_date` datetime DEFAULT '0001-01-01 00:00:00' COMMENT 'Sell start date',
  `end_date` datetime DEFAULT '0001-01-01 00:00:00' COMMENT 'Sell end date',
  `type` tinyint unsigned DEFAULT '0' COMMENT 'Currency Type',
  `price` int unsigned DEFAULT '0' COMMENT 'Sell price',
  `remain` int unsigned DEFAULT '0' COMMENT 'Remaining stock',
  `bonus_type` int unsigned DEFAULT '0' COMMENT 'Bonus type',
  `bouns_count` int unsigned DEFAULT '0' COMMENT 'Bonus amount',
  `cmd_ui` tinyint unsigned DEFAULT '0' COMMENT 'Whether to restrict one person at a time',
  `item_count` int unsigned DEFAULT '1' COMMENT 'Number of bundles',
  `select_type` tinyint unsigned DEFAULT '0',
  `default_flag` tinyint unsigned DEFAULT '0',
  `event_type` tinyint unsigned DEFAULT '0' COMMENT 'Event type',
  `event_date` datetime DEFAULT '0001-01-01 00:00:00' COMMENT 'Event time',
  `dis_price` int unsigned DEFAULT '0' COMMENT 'Current selling price',
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='In-game cashshop listings';


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
  `expirience` int NOT NULL,
  `recoverable_exp` int NOT NULL,
  `hp` int NOT NULL,
  `mp` int NOT NULL,
  `labor_power` mediumint NOT NULL,
  `labor_power_modified` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
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
  `money` bigint NOT NULL,
  `money2` bigint NOT NULL,
  `honor_point` int NOT NULL,
  `vocation_point` int NOT NULL,
  `crime_point` int NOT NULL,
  `crime_record` int NOT NULL,
  `hostile_faction_kills` int NOT NULL DEFAULT '0',
  `pvp_honor` int NOT NULL DEFAULT '0',
  `delete_request_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `transfer_request_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `delete_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `bm_point` int NOT NULL,
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
  `item_id` bigint unsigned NOT NULL DEFAULT '0' COMMENT 'Item DB Id of the associated item',
  `house_id` int unsigned NOT NULL DEFAULT '0' COMMENT 'House DB Id if it is on actual house land',
  `parent_doodad` int unsigned NOT NULL DEFAULT '0' COMMENT 'doodads DB Id this object is standing on',
  `item_template_id` int unsigned NOT NULL DEFAULT '0' COMMENT 'ItemTemplateId of associated item',
  `item_container_id` int unsigned NOT NULL DEFAULT '0' COMMENT 'ItemContainer Id for Coffers',
  `data` int NOT NULL DEFAULT '0' COMMENT 'Doodad specific data',
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


DROP TABLE IF EXISTS `items`;
CREATE TABLE `items` (
  `id` bigint unsigned NOT NULL,
  `type` varchar(100) NOT NULL,
  `template_id` int unsigned NOT NULL,
  `container_id` int unsigned NOT NULL DEFAULT '0',
  `slot_type` enum('Equipment','Inventory','Bank','Trade','Mail','System') NOT NULL,
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
  `slot_type` enum('Equipment','Inventory','Bank','Trade','Mail','System') NOT NULL COMMENT 'Internal Container Type',
  `container_size` int NOT NULL DEFAULT '50' COMMENT 'Maximum Container Size',
  `owner_id` int unsigned NOT NULL COMMENT 'Owning Character Id',
  PRIMARY KEY (`container_id`) 
) COLLATE 'utf8mb4_general_ci';