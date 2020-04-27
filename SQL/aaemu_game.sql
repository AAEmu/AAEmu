CREATE TABLE `abilities` (
  `id` tinyint(3) unsigned NOT NULL,
  `exp` int(11) NOT NULL,
  `owner` int(11) unsigned NOT NULL,
  PRIMARY KEY (`id`,`owner`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;

CREATE TABLE `actabilities` (
  `id` int(10) unsigned NOT NULL,
  `point` int(10) unsigned NOT NULL DEFAULT '0',
  `step` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `owner` int(10) unsigned NOT NULL,
  PRIMARY KEY (`owner`,`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;

CREATE TABLE `appellations` (
  `id` int(10) unsigned NOT NULL,
  `active` tinyint(1) NOT NULL DEFAULT '0',
  `owner` int(10) unsigned NOT NULL,
  PRIMARY KEY (`id`,`owner`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;

CREATE TABLE `auction_house` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `duration` tinyint(3) NOT NULL,
  `item_id` int(11) NOT NULL,
  `object_id` int(11) NOT NULL,
  `grade` tinyint(1) NOT NULL,
  `flags` tinyint(1) NOT NULL,
  `stack_size` int(11) NOT NULL,
  `detail_type` tinyint(1) NOT NULL,
  `creation_time` datetime NOT NULL,
  `lifespan_mins` int(11) NOT NULL,
  `type_1` int(11) NOT NULL,
  `world_id` tinyint(2) NOT NULL,
  `unsecure_date_time` varchar(45) NOT NULL,
  `unpack_date_time` varchar(45) NOT NULL,
  `world_id_2` tinyint(2) NOT NULL,
  `type_2` int(11) NOT NULL,
  `client_name` varchar(45) NOT NULL,
  `start_money` int(11) NOT NULL,
  `direct_money` int(11) NOT NULL,
  `time_left` int(11) NOT NULL,
  `bid_world_id` tinyint(1) NOT NULL,
  `type_3` int(11) NOT NULL,
  `bidder_name` varchar(45) NOT NULL,
  `bid_money` int(11) NOT NULL,
  `extra` int(11) NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=13 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `blocked` (
  `owner` int(11) NOT NULL,
  `blocked_id` int(11) NOT NULL,
  PRIMARY KEY (`owner`,`blocked_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;

CREATE TABLE `cash_shop_item` (
  `id` int(10) unsigned NOT NULL AUTO_INCREMENT COMMENT 'shop_id',
  `uniq_id` int(10) unsigned DEFAULT '0' COMMENT 'ΨһID',
  `cash_name` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL COMMENT '?????',
  `main_tab` tinyint(3) unsigned DEFAULT '1' COMMENT '??????1-6',
  `sub_tab` tinyint(3) unsigned DEFAULT '1' COMMENT '?ӷ???1-7',
  `level_min` tinyint(3) unsigned DEFAULT '0' COMMENT '?ȼ????',
  `level_max` tinyint(3) unsigned DEFAULT '0' COMMENT '?ȼ????',
  `item_template_id` int(10) unsigned DEFAULT '0' COMMENT '??Ʒģ??id',
  `is_sell` tinyint(1) unsigned DEFAULT '0' COMMENT '?Ƿ??',
  `is_hidden` tinyint(1) unsigned DEFAULT '0' COMMENT '?Ƿ??',
  `limit_type` tinyint(3) unsigned DEFAULT '0',
  `buy_count` smallint(5) unsigned DEFAULT '0',
  `buy_type` tinyint(3) unsigned DEFAULT '0',
  `buy_id` int(10) unsigned DEFAULT '0',
  `start_date` datetime DEFAULT '0001-01-01 00:00:00' COMMENT '???ۿ?ʼ',
  `end_date` datetime DEFAULT '0001-01-01 00:00:00' COMMENT '???۽?ֹ',
  `type` tinyint(3) unsigned DEFAULT '0' COMMENT '???????',
  `price` int(10) unsigned DEFAULT '0' COMMENT '?۸',
  `remain` int(10) unsigned DEFAULT '0' COMMENT 'ʣ???',
  `bonus_type` int(10) unsigned DEFAULT '0' COMMENT '???????',
  `bouns_count` int(10) unsigned DEFAULT '0' COMMENT '?????',
  `cmd_ui` tinyint(1) unsigned DEFAULT '0' COMMENT '?Ƿ?????һ??һ?',
  `item_count` int(10) unsigned DEFAULT '1' COMMENT '?????',
  `select_type` tinyint(3) unsigned DEFAULT '0',
  `default_flag` tinyint(3) unsigned DEFAULT '0',
  `event_type` tinyint(3) unsigned DEFAULT '0' COMMENT '????',
  `event_date` datetime DEFAULT '0001-01-01 00:00:00' COMMENT '?ʱ?',
  `dis_price` int(10) unsigned DEFAULT '0' COMMENT '??ǰ?ۼ',
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci ROW_FORMAT=DYNAMIC COMMENT='?˱??????ڴ????е??ֶβ?ȥ???ظ??ֶ????ɡ??ֶ????ƺ??????Դ???Ϊ׼??';

CREATE TABLE `characters` (
  `id` int(11) unsigned NOT NULL,
  `account_id` int(11) unsigned NOT NULL,
  `name` varchar(128) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `access_level` int(3) unsigned NOT NULL DEFAULT '0',
  `race` tinyint(2) NOT NULL,
  `gender` tinyint(1) NOT NULL,
  `unit_model_params` blob NOT NULL,
  `level` tinyint(4) NOT NULL,
  `expirience` int(11) NOT NULL,
  `recoverable_exp` int(11) NOT NULL,
  `hp` int(11) NOT NULL,
  `mp` int(11) NOT NULL,
  `labor_power` mediumint(9) NOT NULL,
  `labor_power_modified` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `consumed_lp` int(11) NOT NULL,
  `ability1` tinyint(4) NOT NULL,
  `ability2` tinyint(4) NOT NULL,
  `ability3` tinyint(4) NOT NULL,
  `world_id` int(11) unsigned NOT NULL,
  `zone_id` int(11) unsigned NOT NULL,
  `x` float NOT NULL,
  `y` float NOT NULL,
  `z` float NOT NULL,
  `rotation_x` tinyint(4) NOT NULL,
  `rotation_y` tinyint(4) NOT NULL,
  `rotation_z` tinyint(4) NOT NULL,
  `faction_id` int(11) unsigned NOT NULL,
  `faction_name` varchar(128) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `expedition_id` int(11) NOT NULL,
  `family` int(11) unsigned NOT NULL,
  `dead_count` mediumint(8) unsigned NOT NULL,
  `dead_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `rez_wait_duration` int(11) NOT NULL,
  `rez_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `rez_penalty_duration` int(11) NOT NULL,
  `leave_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `money` bigint(20) NOT NULL,
  `money2` bigint(20) NOT NULL,
  `honor_point` int(11) NOT NULL,
  `vocation_point` int(11) NOT NULL,
  `crime_point` int(11) NOT NULL,
  `crime_record` int(11) NOT NULL,
  `delete_request_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `transfer_request_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `delete_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `bm_point` int(11) NOT NULL,
  `auto_use_aapoint` tinyint(1) NOT NULL,
  `prev_point` int(11) NOT NULL,
  `point` int(11) NOT NULL,
  `gift` int(11) NOT NULL,
  `num_inv_slot` tinyint(3) unsigned NOT NULL DEFAULT '50',
  `num_bank_slot` smallint(5) unsigned NOT NULL DEFAULT '50',
  `expanded_expert` tinyint(4) NOT NULL,
  `slots` blob NOT NULL,
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `deleted` int(11) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`,`account_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;

CREATE TABLE `completed_quests` (
  `id` int(11) unsigned NOT NULL,
  `data` tinyblob NOT NULL,
  `owner` int(11) unsigned NOT NULL,
  PRIMARY KEY (`id`,`owner`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

CREATE TABLE `expedition_members` (
  `character_id` int(11) NOT NULL,
  `expedition_id` int(11) NOT NULL,
  `name` varchar(128) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `level` tinyint(4) unsigned NOT NULL,
  `role` tinyint(4) unsigned NOT NULL,
  `last_leave_time` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `ability1` tinyint(4) unsigned NOT NULL,
  `ability2` tinyint(4) unsigned NOT NULL,
  `ability3` tinyint(4) unsigned NOT NULL,
  `memo` varchar(128) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  PRIMARY KEY (`character_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;

CREATE TABLE `expedition_role_policies` (
  `expedition_id` int(11) NOT NULL,
  `role` tinyint(4) unsigned NOT NULL,
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
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;

CREATE TABLE `expeditions` (
  `id` int(11) NOT NULL,
  `owner` int(11) NOT NULL,
  `owner_name` varchar(128) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `name` varchar(128) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `mother` int(11) NOT NULL,
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`,`owner`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;

CREATE TABLE `family_members` (
  `character_id` int(11) NOT NULL,
  `family_id` int(11) NOT NULL,
  `name` varchar(45) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `role` tinyint(1) NOT NULL DEFAULT '0',
  `title` varchar(45) CHARACTER SET utf8 COLLATE utf8_general_ci DEFAULT NULL,
  PRIMARY KEY (`family_id`,`character_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;

CREATE TABLE `friends` (
  `id` int(11) NOT NULL,
  `friend_id` int(11) NOT NULL,
  `owner` int(11) NOT NULL,
  PRIMARY KEY (`id`,`owner`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;

CREATE TABLE `housings` (
  `id` int(11) NOT NULL,
  `account_id` int(10) unsigned NOT NULL,
  `owner` int(10) unsigned NOT NULL,
  `co_owner` int(10) unsigned NOT NULL,
  `template_id` int(10) unsigned NOT NULL,
  `name` varchar(128) NOT NULL,
  `x` float NOT NULL,
  `y` float NOT NULL,
  `z` float NOT NULL,
  `rotation_z` tinyint(4) NOT NULL,
  `current_step` tinyint(4) NOT NULL,
  `current_action` int(11) NOT NULL DEFAULT '0',
  `permission` tinyint(4) NOT NULL,
  PRIMARY KEY (`account_id`,`owner`,`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

CREATE TABLE `items` (
  `id` bigint(20) unsigned NOT NULL,
  `type` varchar(100) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `template_id` int(11) unsigned NOT NULL,
  `slot_type` enum('Equipment','Inventory','Bank') CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `slot` int(11) NOT NULL,
  `count` int(11) NOT NULL,
  `details` blob,
  `lifespan_mins` int(11) NOT NULL,
  `made_unit_id` int(11) unsigned NOT NULL DEFAULT '0',
  `unsecure_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `unpack_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `owner` int(11) unsigned NOT NULL,
  `grade` tinyint(1) DEFAULT '0',
  `created_at` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `bounded` tinyint(1) DEFAULT '0',
  PRIMARY KEY (`id`) USING BTREE,
  KEY `owner` (`owner`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;

CREATE TABLE `mates` (
  `id` int(11) unsigned NOT NULL,
  `item_id` bigint(20) unsigned NOT NULL,
  `name` text CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `xp` int(11) NOT NULL,
  `level` tinyint(4) NOT NULL,
  `mileage` int(11) NOT NULL,
  `hp` int(11) NOT NULL,
  `mp` int(11) NOT NULL,
  `owner` int(11) unsigned NOT NULL,
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`,`item_id`,`owner`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;

CREATE TABLE `options` (
  `key` varchar(100) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `value` text CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `owner` int(11) unsigned NOT NULL,
  PRIMARY KEY (`key`,`owner`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;

CREATE TABLE `portal_book_coords` (
  `id` int(11) NOT NULL,
  `name` varchar(128) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `x` int(11) DEFAULT '0',
  `y` int(11) DEFAULT '0',
  `z` int(11) DEFAULT '0',
  `zone_id` int(11) DEFAULT '0',
  `z_rot` int(11) DEFAULT '0',
  `sub_zone_id` int(11) DEFAULT '0',
  `owner` int(11) NOT NULL,
  PRIMARY KEY (`id`,`owner`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;

CREATE TABLE `portal_visited_district` (
  `id` int(11) NOT NULL,
  `subzone` int(11) NOT NULL,
  `owner` int(11) NOT NULL,
  PRIMARY KEY (`id`,`subzone`,`owner`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;

CREATE TABLE `quests` (
  `id` int(11) unsigned NOT NULL,
  `template_id` int(11) unsigned NOT NULL,
  `data` tinyblob NOT NULL,
  `status` tinyint(4) NOT NULL,
  `owner` int(11) unsigned NOT NULL,
  PRIMARY KEY (`id`,`owner`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

CREATE TABLE `skills` (
  `id` int(11) unsigned NOT NULL,
  `level` tinyint(4) NOT NULL,
  `type` enum('Skill','Buff') CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `owner` int(11) unsigned NOT NULL,
  PRIMARY KEY (`id`,`owner`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;
