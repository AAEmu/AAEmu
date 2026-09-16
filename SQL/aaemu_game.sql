CREATE DATABASE IF NOT EXISTS `aaemu_game`;
USE aaemu_game;
-- ----------------------------------------------------------------------------------------------
-- Make sure to remove the above two lines if you want use your own DB/Schema names during import
-- This script is idempotent. It can be run multiple times without causing errors, and does not
-- clear data from existing tables.
-- ----------------------------------------------------------------------------------------------

SET NAMES utf8;
SET time_zone = '+00:00';
SET foreign_key_checks = 0;

CREATE TABLE IF NOT EXISTS `abilities` (
  `id` tinyint unsigned NOT NULL,
  `exp` int NOT NULL,
  `owner` int unsigned NOT NULL,
  PRIMARY KEY (`id`,`owner`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Skillsets Exp';

CREATE TABLE IF NOT EXISTS `ability_sets` (
  `owner` int unsigned NOT NULL,
  `slot` tinyint unsigned NOT NULL,
  `ability1` tinyint unsigned NOT NULL DEFAULT 30,
  `ability2` tinyint unsigned NOT NULL DEFAULT 30,
  `ability3` tinyint unsigned NOT NULL DEFAULT 30,
  PRIMARY KEY (`owner`, `slot`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Saved skillsaver ability triads';

CREATE TABLE IF NOT EXISTS `ability_set_skills` (
  `owner` int unsigned NOT NULL,
  `slot` tinyint unsigned NOT NULL,
  `skill_id` int unsigned NOT NULL,
  `is_passive` tinyint(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`owner`, `slot`, `skill_id`, `is_passive`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Skills/passives snapshotted into a skillsaver slot';

CREATE TABLE IF NOT EXISTS `character_bless_uthstin` (
  `owner` int unsigned NOT NULL,
  `select_page_index` int NOT NULL DEFAULT 0,
  `extend_max_stats` int NOT NULL DEFAULT 0,
  `apply_extend_count` int NOT NULL DEFAULT 0,
  PRIMARY KEY (`owner`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Bless Uthstin header (selected page and cap)';

CREATE TABLE IF NOT EXISTS `character_bless_uthstin_pages` (
  `owner` int unsigned NOT NULL,
  `page_index` tinyint unsigned NOT NULL,
  `str` int NOT NULL DEFAULT 0,
  `dex` int NOT NULL DEFAULT 0,
  `sta` int NOT NULL DEFAULT 0,
  `int` int NOT NULL DEFAULT 0,
  `spi` int NOT NULL DEFAULT 0,
  `apply_normal` int NOT NULL DEFAULT 0,
  `apply_special` int NOT NULL DEFAULT 0,
  PRIMARY KEY (`owner`, `page_index`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Bless Uthstin applied stats per page';

CREATE TABLE IF NOT EXISTS `character_equip_slot_reinforces` (
  `owner` int unsigned NOT NULL,
  `slot_type_id` tinyint unsigned NOT NULL,
  `level` tinyint NOT NULL DEFAULT 0,
  `exp` int NOT NULL DEFAULT 0,
  `level_effect_index` int NOT NULL DEFAULT -1,
  PRIMARY KEY (`owner`, `slot_type_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Equip slot reinforcement level, exp and chosen level effect';

CREATE TABLE IF NOT EXISTS `character_butlers` (
  `character_id` int unsigned NOT NULL,
  `house_id` int unsigned DEFAULT NULL,
  `name` varchar(128) NOT NULL DEFAULT '',
  `labor_power` int unsigned NOT NULL DEFAULT 0,
  `lp_charged_amount` smallint unsigned NOT NULL DEFAULT 0,
  `lp_charge_reset_time` bigint NOT NULL DEFAULT 0,
  `remain_production_cost` smallint unsigned NOT NULL DEFAULT 0,
  PRIMARY KEY (`character_id`),
  UNIQUE KEY `ux_character_butlers_house` (`house_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Character-owned farmhand state and durable house binding';

CREATE TABLE IF NOT EXISTS `character_butler_permanent_data` (
  `character_id` int unsigned NOT NULL,
  `data_key` tinyint NOT NULL,
  `data_value` bigint unsigned NOT NULL,
  PRIMARY KEY (`character_id`, `data_key`),
  CONSTRAINT `fk_character_butler_permanent_data_butler`
    FOREIGN KEY (`character_id`) REFERENCES `character_butlers` (`character_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Farmhand permanent-data map serialized to the client';

CREATE TABLE IF NOT EXISTS `character_butler_harvest_jobs` (
  `id` bigint NOT NULL AUTO_INCREMENT,
  `character_id` int unsigned NOT NULL,
  `static_harvest_id` int unsigned NOT NULL,
  `requested_amount` smallint unsigned NOT NULL,
  `remaining_repeat_count` smallint unsigned NOT NULL,
  `lp_for_calc_exp` int unsigned NOT NULL,
  `update_time` bigint NOT NULL,
  PRIMARY KEY (`id`),
  KEY `idx_character_butler_harvest_jobs_character` (`character_id`),
  CONSTRAINT `fk_character_butler_harvest_jobs_butler`
    FOREIGN KEY (`character_id`) REFERENCES `character_butlers` (`character_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Durable farmhand crop and livestock jobs';

CREATE TABLE IF NOT EXISTS `character_butler_harvest_completions` (
  `job_id` bigint NOT NULL,
  `cycle_number` smallint unsigned NOT NULL,
  `completed_at` bigint NOT NULL,
  PRIMARY KEY (`job_id`, `cycle_number`),
  CONSTRAINT `fk_character_butler_harvest_completions_job`
    FOREIGN KEY (`job_id`) REFERENCES `character_butler_harvest_jobs` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Idempotence markers for farmhand harvest cycles';

CREATE TABLE IF NOT EXISTS `character_butler_items` (
  `character_id` int unsigned NOT NULL,
  `item_type` tinyint unsigned NOT NULL,
  `item_id` bigint unsigned NOT NULL,
  PRIMARY KEY (`character_id`, `item_id`),
  UNIQUE KEY `ux_character_butler_items_item` (`item_id`),
  CONSTRAINT `fk_character_butler_items_butler`
    FOREIGN KEY (`character_id`) REFERENCES `character_butlers` (`character_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Logical farmhand locations for actual items held in System containers';

CREATE TABLE IF NOT EXISTS `character_quest_cinema_end_effects` (
  `owner` int unsigned NOT NULL,
  `quest_id` int unsigned NOT NULL,
  `cinema_id` int unsigned NOT NULL,
  `component_id` int unsigned NOT NULL,
  PRIMARY KEY (`owner`, `component_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Quest cinema-end effects still owed to the character';

CREATE TABLE IF NOT EXISTS `character_arche_passes` (
  `owner` int unsigned NOT NULL,
  `pass_id` int unsigned NOT NULL,
  `status` tinyint unsigned NOT NULL DEFAULT 0,
  `point` bigint NOT NULL DEFAULT 0,
  `premium` tinyint(1) NOT NULL DEFAULT 0,
  `last_reward_tier` int unsigned NOT NULL DEFAULT 0,
  `last_premium_reward_tier` int unsigned NOT NULL DEFAULT 0,
  PRIMARY KEY (`owner`, `pass_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Arche Pass ownership and progress';

CREATE TABLE IF NOT EXISTS `character_arche_pass_missions` (
  `owner` int unsigned NOT NULL,
  `complete_used` int unsigned NOT NULL DEFAULT 0,
  `change_used` int unsigned NOT NULL DEFAULT 0,
  `week_start` date NOT NULL,
  PRIMARY KEY (`owner`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Arche Pass weekly mission counters';

CREATE TABLE IF NOT EXISTS `account_attendances` (
  `account_id` int unsigned NOT NULL,
  `year` smallint NOT NULL,
  `month` tinyint NOT NULL,
  `day` tinyint NOT NULL,
  `attended_at` bigint NOT NULL,
  `is_archelife` tinyint NOT NULL DEFAULT 0,
  PRIMARY KEY (`account_id`, `year`, `month`, `day`),
  KEY `idx_account_month` (`account_id`, `year`, `month`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Event Center attendance claims';

CREATE TABLE IF NOT EXISTS `account_schedule_items` (
  `account_id` int unsigned NOT NULL,
  `schedule_id` int NOT NULL,
  `gave` tinyint unsigned NOT NULL DEFAULT 0,
  `cumulated` bigint NOT NULL DEFAULT 0,
  `updated` bigint NOT NULL,
  PRIMARY KEY (`account_id`, `schedule_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='HUD schedule-item timers';


CREATE TABLE IF NOT EXISTS `accounts` (
  `account_id` INT(11) NOT NULL,
  `access_level` INT(11) NOT NULL DEFAULT '0',
  `labor` INT(11) NOT NULL DEFAULT '0',
  `local_labor` INT UNSIGNED NOT NULL DEFAULT '0',
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


CREATE TRIGGER IF NOT EXISTS update_timestamps BEFORE UPDATE ON accounts
FOR EACH ROW
   SET NEW.last_updated = UTC_TIMESTAMP();


CREATE TABLE IF NOT EXISTS `actabilities` (
  `id` int unsigned NOT NULL,
  `point` int unsigned NOT NULL DEFAULT '0',
  `step` tinyint unsigned NOT NULL DEFAULT '0',
  `owner` int unsigned NOT NULL,
  PRIMARY KEY (`owner`,`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Vocations';


CREATE TABLE IF NOT EXISTS `appellations` (
  `id` int unsigned NOT NULL,
  `active` tinyint(1) NOT NULL DEFAULT '0',
  `owner` int unsigned NOT NULL,
  PRIMARY KEY (`id`,`owner`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Earned titles';


-- ----------------------------------
-- Table structure for auction_house
-- ----------------------------------
CREATE TABLE IF NOT EXISTS `auction_house` (
	`id` BIGINT(20) NOT NULL AUTO_INCREMENT,
	`duration` TINYINT(4) NOT NULL,
	`item_id` BIGINT(20) NOT NULL,
	`post_date` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Time when the auction item was put up for sale (in UTC)',
	`stack_size` INT(11) NOT NULL,
	`end_time` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Time when the sale period ends (in UTC)',
	`world_id` TINYINT(4) NOT NULL,
	`client_id` INT(11) NOT NULL,
	`client_name` VARCHAR(45) NOT NULL COLLATE 'utf8mb4_general_ci',
	`start_money` BIGINT(20) NOT NULL,
	`direct_money` BIGINT(20) NOT NULL,
	`asked` BIGINT(20) UNSIGNED NOT NULL DEFAULT 0,
	`charge_percent` INT(11) NOT NULL DEFAULT 0,
	`deposit_percent` INT(11) NOT NULL DEFAULT 0,
	`service_kind` TINYINT(4) NOT NULL DEFAULT 0,
	`bid_world_id` INT(11) NOT NULL,
	`bidder_id` INT(11) NOT NULL,
	`bidder_name` VARCHAR(45) NOT NULL COLLATE 'utf8mb4_general_ci',
	`bid_money` BIGINT(20) NOT NULL,
	`extra` BIGINT(20) NOT NULL,
	`min_stack` INT(11) NOT NULL DEFAULT 1,
	`max_stack` INT(11) NOT NULL DEFAULT 1,
	PRIMARY KEY (`id`) USING BTREE
)
COMMENT='Listed AH Items'
COLLATE='utf8mb4_general_ci'
ENGINE=InnoDB
ROW_FORMAT=DYNAMIC
;

CREATE TABLE IF NOT EXISTS `auction_sold_records` (
	`id` BIGINT(20) NOT NULL AUTO_INCREMENT,
	`item_template_id` INT UNSIGNED NOT NULL,
	`item_grade` TINYINT UNSIGNED NOT NULL,
	`sold_at` DATETIME NOT NULL,
	`price` BIGINT(20) NOT NULL,
	`stack` INT(11) NOT NULL,
	PRIMARY KEY (`id`) USING BTREE,
	INDEX `idx_sold_lookup` (`item_template_id`, `item_grade`, `sold_at`)
)
COMMENT='Auction house sold-price history'
COLLATE='utf8mb4_general_ci'
ENGINE=InnoDB
ROW_FORMAT=DYNAMIC
;


CREATE TABLE IF NOT EXISTS `blocked` (
  `owner` int NOT NULL,
  `blocked_id` int NOT NULL,
  PRIMARY KEY (`owner`,`blocked_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;


CREATE TABLE IF NOT EXISTS `character_active_buffs` (
  `character_id`  INT UNSIGNED NOT NULL COMMENT 'Character who owns this buff',
  `buff_id`       INT UNSIGNED NOT NULL COMMENT 'BuffTemplate.Id from game data',
  `caster_id`     INT UNSIGNED NOT NULL COMMENT 'Character who originally cast this buff',
  `skill_id`      INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Source skill template ID (0 if none)',
  `ab_level`      INT UNSIGNED NOT NULL DEFAULT 1 COMMENT 'Ability level for bonus scaling',
  `duration`      INT NOT NULL COMMENT 'Total duration in milliseconds',
  `time_left`     INT NOT NULL COMMENT 'Remaining time in milliseconds at save',
  `charge`        INT NOT NULL DEFAULT 0 COMMENT 'Current charge count',
  `stack_count`   INT NOT NULL DEFAULT 1 COMMENT 'Number of stacks',
  `real_time`     TINYINT UNSIGNED NOT NULL DEFAULT 0 COMMENT '1=timer ticks offline, 0=timer paused offline',
  `saved_at`      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'UTC timestamp when buff was saved',
  PRIMARY KEY (`character_id`, `buff_id`),
  INDEX `idx_character_id` (`character_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Stores active buffs across player sessions';


CREATE TABLE IF NOT EXISTS `character_favorite_crafts` (
  `owner` int unsigned NOT NULL COMMENT 'Character id',
  `craft_type` int unsigned NOT NULL COMMENT 'Craft recipe id from game content',
  PRIMARY KEY (`owner`,`craft_type`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Per-character favorite crafting recipes';


CREATE TABLE IF NOT EXISTS `character_recipes` (
  `owner` int unsigned NOT NULL COMMENT 'Character id',
  `craft_id` int unsigned NOT NULL COMMENT 'Craft id from game content, learned from the item_recipes entry of a recipe item',
  `learned_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'When the recipe item was used',
  PRIMARY KEY (`owner`,`craft_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Recipes a character has learned by using a recipe item';


CREATE TABLE IF NOT EXISTS `character_skill_active_types` (
  `owner` int unsigned NOT NULL COMMENT 'Character id',
  `heir_skill_type` int unsigned NOT NULL COMMENT 'Client Heir-skill category key',
  `skill_type` int unsigned NOT NULL COMMENT 'Client skill entry key',
  `active_type` tinyint unsigned NOT NULL COMMENT 'SkillActiveType value',
  PRIMARY KEY (`owner`,`heir_skill_type`,`skill_type`) USING BTREE,
  KEY `idx_character_skill_active_types_owner` (`owner`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Per-character skill visibility and activation state';


CREATE TABLE IF NOT EXISTS `heir_skill_activations` (
  `owner` int unsigned NOT NULL COMMENT 'Character id',
  `heir_skill_id` int unsigned NOT NULL COMMENT 'Selected heir_skills content row',
  `successor_skill_id` int unsigned NOT NULL COMMENT 'Selected heir_skill_details skill_id',
  PRIMARY KEY (`owner`,`heir_skill_id`) USING BTREE,
  KEY `idx_heir_skill_activations_owner` (`owner`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Active Heir-skill successor selections';


CREATE TABLE IF NOT EXISTS `character_merchant_purchases` (
  `character_id` INT UNSIGNED NOT NULL,
  `item_id` INT UNSIGNED NOT NULL COMMENT 'Item template type used as the native client map key',
  `buy_count` INT UNSIGNED NOT NULL DEFAULT 0,
  `purchase_type` TINYINT UNSIGNED NOT NULL COMMENT '1 always, 2 daily, 3 weekly, 4 monthly',
  `period_start` DATETIME NOT NULL COMMENT 'UTC start of the active limit period',
  PRIMARY KEY (`character_id`, `item_id`),
  INDEX `idx_merchant_purchase_type` (`purchase_type`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Persistent per-character merchant purchase limits';


CREATE TABLE IF NOT EXISTS `characters` (
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
  `heir_exp` bigint(20) unsigned NOT NULL DEFAULT '0',
  `hp` int NOT NULL,
  `mp` int NOT NULL,
  `consumed_lp` int NOT NULL,
  `local_lp` int unsigned NOT NULL DEFAULT '0',
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
  `expedition_rejoin_until` bigint NOT NULL DEFAULT '0',
  `family` int unsigned NOT NULL,
  `family_rejoin_until` bigint NOT NULL DEFAULT '0',
  `dead_count` mediumint unsigned NOT NULL,
  `dead_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `rez_wait_duration` int NOT NULL,
  `rez_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `rez_penalty_duration` int NOT NULL,
  `leave_time` datetime NOT NULL DEFAULT '0001-01-01 00:00:00',
  `money` bigint NOT NULL DEFAULT '0',
  `aa_point` bigint NOT NULL DEFAULT '0',
  `money2` bigint NOT NULL DEFAULT '0',
  `bank_aa_point` bigint NOT NULL DEFAULT '0',
  `honor_point` int NOT NULL DEFAULT '0',
  `vocation_point` int NOT NULL DEFAULT '0',
  `crime_point` int NOT NULL DEFAULT '0',
  `crime_record` int NOT NULL DEFAULT '0',
  `jury_point` int NOT NULL DEFAULT '0',
  `hostile_faction_kills` int NOT NULL DEFAULT '0',
  `pvp_honor` int NOT NULL DEFAULT '0',
  `died_in_pvp` tinyint(1) NOT NULL DEFAULT '0',
  `died_in_pvp_war_zone` tinyint(1) NOT NULL DEFAULT '0',
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
  `usable_abil_set_slot_count` tinyint unsigned NOT NULL DEFAULT '1',
  `used_free_abil_set_activation` tinyint unsigned NOT NULL DEFAULT '0',
  `slots` blob NOT NULL,
  `created_at` datetime(0) NOT NULL DEFAULT CURRENT_TIMESTAMP(0),
  `updated_at` datetime(0) NOT NULL DEFAULT '0001-01-01 00:00:00',
  `deleted` int(11) NOT NULL DEFAULT 0,
  `return_district` int(11) NOT NULL DEFAULT 0,
  `online_time` INT(11) NOT NULL DEFAULT 0 COMMENT 'Time that the character has been online',
  `total_play_time` int unsigned NOT NULL DEFAULT '0',
  `privacy_status` tinyint NOT NULL DEFAULT '0',
  `represent` tinyint(1) NOT NULL DEFAULT 0 COMMENT 'Is this the account main (represent) character',
  PRIMARY KEY (`id`, `account_id`) USING BTREE
) ENGINE = InnoDB CHARACTER SET = utf8 COLLATE = utf8_general_ci COMMENT = 'Basic player character data' ROW_FORMAT = DYNAMIC;


CREATE TABLE IF NOT EXISTS `completed_quests` (
  `id` int unsigned NOT NULL,
  `data` tinyblob NOT NULL,
  `owner` int unsigned NOT NULL,
  PRIMARY KEY (`id`,`owner`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Quests marked as completed for character';


CREATE TABLE IF NOT EXISTS `doodads` (
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


CREATE TABLE IF NOT EXISTS `expedition_members` (
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
  `contribution_point` int unsigned NOT NULL DEFAULT '0',
  `weekly_contribution_point` int unsigned NOT NULL DEFAULT '0',
  `weekly_contribution_period_start` date NOT NULL DEFAULT '1970-01-05',
  PRIMARY KEY (`character_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Guild members';


CREATE TABLE IF NOT EXISTS `expedition_role_policies` (
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
  `use_instance` tinyint(1) NOT NULL DEFAULT '0',
  PRIMARY KEY (`expedition_id`,`role`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Guild role settings';


-- Guild-level prestige-shop buff purchases: which grade of each expedition_buffs/expedition_buff_grades
-- row (game data, shipped in compact.sqlite3) a guild has purchased. 0/no row = not purchased at all.
CREATE TABLE IF NOT EXISTS `expedition_buff_purchases` (
  `expedition_id` int unsigned NOT NULL,
  `expedition_buff_id` int unsigned NOT NULL COMMENT 'expedition_buffs.id (game data)',
  `grade` tinyint unsigned NOT NULL DEFAULT '0' COMMENT 'highest purchased expedition_buff_grades.grade for this buff',
  PRIMARY KEY (`expedition_id`, `expedition_buff_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Guild-level prestige-shop buff purchases';


CREATE TABLE IF NOT EXISTS `expeditions` (
  `id` int NOT NULL,
  `owner` int NOT NULL,
  `owner_name` varchar(128) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `name` varchar(128) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
  `mother` int NOT NULL,
  `level` int unsigned NOT NULL DEFAULT '1',
  `exp` int unsigned NOT NULL DEFAULT '0',
  `daily_exp` int unsigned NOT NULL DEFAULT '0',
  `last_exp_update_time` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `notice` varchar(800) NOT NULL DEFAULT '',
  `residence_house_id` int unsigned NOT NULL DEFAULT '0' COMMENT 'Guild Residence house id, 0 = none placed',
  `interest` smallint NOT NULL DEFAULT '0' COMMENT 'recruitment-board interest tag bitmask',
  `war_deposit` int unsigned NOT NULL DEFAULT '0',
  `war_wins` int unsigned NOT NULL DEFAULT '0',
  `war_losses` int unsigned NOT NULL DEFAULT '0',
  `war_draws` int unsigned NOT NULL DEFAULT '0',
  `daily_contribution_point` int unsigned NOT NULL DEFAULT '0',
  `last_contribution_point_added` datetime NOT NULL DEFAULT '1970-01-01 00:00:00',
  `last_assignment_update_time` datetime NOT NULL DEFAULT '1970-01-01 00:00:00',
  `war_enemy_expedition_id` int unsigned NOT NULL DEFAULT '0',
  `war_declared_at` datetime NULL DEFAULT NULL,
  `war_protected_until` datetime NULL DEFAULT NULL,
  `war_ends_at` datetime NULL DEFAULT NULL,
  `war_kill_score` int unsigned NOT NULL DEFAULT '0',
  `war_is_declarer` tinyint(1) NOT NULL DEFAULT '0',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Guilds';

CREATE TABLE IF NOT EXISTS `character_today_board_reset_counts` (
  `owner` int unsigned NOT NULL,
  `sort_id` int NOT NULL,
  `day_key` date NOT NULL,
  `resets_used` int unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`owner`,`sort_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Per-board today assignment reset counters';

CREATE TABLE IF NOT EXISTS `expedition_public_assignments` (
  `expedition_id` int NOT NULL,
  `period_start` datetime NOT NULL,
  `real_step` int unsigned NOT NULL,
  `group_id` int unsigned NOT NULL,
  `quest_context_id` int unsigned NOT NULL,
  `status` tinyint NOT NULL,
  `objectives` json NOT NULL,
  `version` int unsigned NOT NULL DEFAULT '0',
  `selection_generation` int unsigned NOT NULL DEFAULT '1',
  `completed_at` datetime NULL,
  `guild_rewarded` tinyint(1) NOT NULL DEFAULT '0',
  PRIMARY KEY (`expedition_id`,`period_start`,`real_step`),
  CONSTRAINT `fk_public_assignment_expedition` FOREIGN KEY (`expedition_id`) REFERENCES `expeditions` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Shared weekly guild assignments';

CREATE TABLE IF NOT EXISTS `expedition_public_assignment_contributors` (
  `expedition_id` int NOT NULL,
  `period_start` datetime NOT NULL,
  `real_step` int unsigned NOT NULL,
  `character_id` int unsigned NOT NULL,
  `character_name` varchar(128) NOT NULL,
  `contribution` bigint unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`expedition_id`,`period_start`,`real_step`,`character_id`),
  CONSTRAINT `fk_public_contributor_assignment` FOREIGN KEY (`expedition_id`,`period_start`,`real_step`) REFERENCES `expedition_public_assignments` (`expedition_id`,`period_start`,`real_step`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Guild assignment contributors';

CREATE TABLE IF NOT EXISTS `expedition_public_assignment_claims` (
  `expedition_id` int NOT NULL,
  `period_start` datetime NOT NULL,
  `real_step` int unsigned NOT NULL,
  `character_id` int unsigned NOT NULL,
  `character_name` varchar(128) NOT NULL,
  `delivered_at` datetime NULL,
  PRIMARY KEY (`expedition_id`,`period_start`,`real_step`,`character_id`),
  CONSTRAINT `fk_public_claim_assignment` FOREIGN KEY (`expedition_id`,`period_start`,`real_step`) REFERENCES `expedition_public_assignments` (`expedition_id`,`period_start`,`real_step`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Durable guild assignment reward delivery';

CREATE TABLE IF NOT EXISTS `expedition_renames` (
  `expedition_id` int unsigned NOT NULL,
  `last_renamed_at` datetime(6) NOT NULL,
  PRIMARY KEY (`expedition_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Guild rename cooldown state';


CREATE TABLE IF NOT EXISTS `expedition_portals` (
  `id` int unsigned NOT NULL AUTO_INCREMENT,
  `expedition_id` int NOT NULL,
  `name` varchar(128) NOT NULL,
  `zone_id` int unsigned NOT NULL,
  `x` float NOT NULL,
  `y` float NOT NULL,
  `z` float NOT NULL,
  `z_rot` float NOT NULL,
  PRIMARY KEY (`id`),
  KEY `idx_expedition_portals_expedition` (`expedition_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Saved guild portal destinations';

CREATE TABLE IF NOT EXISTS `expedition_management_histories` (
  `id` bigint unsigned NOT NULL AUTO_INCREMENT,
  `expedition_id` int NOT NULL,
  `member_name` varchar(128) NOT NULL,
  `history_type` int NOT NULL,
  `amount` bigint unsigned NOT NULL DEFAULT '0',
  `used_at` datetime(6) NOT NULL,
  `detail_id` int unsigned NOT NULL DEFAULT '0',
  `detail_value` int NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`),
  KEY `idx_expedition_management_history` (`expedition_id`,`used_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Guild management activity history';

CREATE TABLE IF NOT EXISTS `expedition_shop_histories` (
  `id` bigint unsigned NOT NULL AUTO_INCREMENT,
  `expedition_id` int NOT NULL,
  `member_name` varchar(128) NOT NULL,
  `item_id` int NOT NULL,
  `stack` int NOT NULL,
  `amount` bigint unsigned NOT NULL DEFAULT '0',
  `purchased_at` datetime(6) NOT NULL,
  PRIMARY KEY (`id`),
  KEY `idx_expedition_shop_history` (`expedition_id`,`purchased_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Guild shop purchase history';

CREATE TABLE IF NOT EXISTS `expedition_war_histories` (
  `id` bigint unsigned NOT NULL AUTO_INCREMENT,
  `declarer_id` int NOT NULL,
  `declarer_name` varchar(128) NOT NULL,
  `defendant_id` int NOT NULL,
  `defendant_name` varchar(128) NOT NULL,
  `declared_at` datetime(6) NOT NULL,
  `declarer_kills` int unsigned NOT NULL DEFAULT '0',
  `defendant_kills` int unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`),
  KEY `idx_expedition_war_declarer` (`declarer_id`,`declared_at`),
  KEY `idx_expedition_war_defendant` (`defendant_id`,`declared_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Retained guild war history';

CREATE TABLE IF NOT EXISTS `expedition_daily_activity` (
  `expedition_id` int NOT NULL,
  `character_id` int unsigned NOT NULL,
  `period_start` datetime(6) NOT NULL,
  `contribution_used` int NOT NULL DEFAULT '0',
  PRIMARY KEY (`expedition_id`,`character_id`,`period_start`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Guild daily activity counters';

CREATE TABLE IF NOT EXISTS `expedition_instance_histories` (
  `history_id` bigint unsigned NOT NULL AUTO_INCREMENT,
  `expedition_id` int NOT NULL,
  `instance_rank_detail_id` int unsigned NOT NULL COMMENT 'instance_rank_details.id; first native history/rating type',
  `instance_id` int unsigned NOT NULL,
  `score` int unsigned NOT NULL,
  `play_result` tinyint unsigned NOT NULL,
  `recorded_at` datetime(6) NOT NULL,
  PRIMARY KEY (`history_id`),
  KEY `idx_expedition_instance_history` (`expedition_id`,`recorded_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Guild battlefield result history';

CREATE TABLE IF NOT EXISTS `expedition_instance_history_members` (
  `history_id` bigint unsigned NOT NULL,
  `character_id` bigint unsigned NOT NULL,
  `status` tinyint unsigned NOT NULL,
  PRIMARY KEY (`history_id`,`character_id`),
  KEY `idx_expedition_instance_history_member_character` (`character_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Guild battlefield result participants';


CREATE TABLE IF NOT EXISTS `families` (
  `id` int unsigned NOT NULL,
  `name` varchar(256) NOT NULL DEFAULT '',
  `notice` varchar(800) NOT NULL DEFAULT '',
  `level` int unsigned NOT NULL DEFAULT '1',
  `exp` int unsigned NOT NULL DEFAULT '0',
  `daily_exp` int unsigned NOT NULL DEFAULT '0',
  `last_exp_update_time` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `increased_member_count` int unsigned NOT NULL DEFAULT '0',
  `reset_time` bigint NOT NULL DEFAULT '0',
  `change_name_time` bigint NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Families';

CREATE TABLE IF NOT EXISTS `family_act_sanctions` (
  `family_id` int unsigned NOT NULL,
  `type` tinyint unsigned NOT NULL,
  `end_time` bigint NOT NULL DEFAULT '0',
  PRIMARY KEY (`family_id`,`type`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Family action sanctions';


CREATE TABLE IF NOT EXISTS `family_members` (
  `character_id` int NOT NULL,
  `family_id` int NOT NULL,
  `name` varchar(45) NOT NULL,
  `role` tinyint(1) NOT NULL DEFAULT '0',
  `role_update_time` bigint NOT NULL DEFAULT '0',
  `login_reward_time` bigint NOT NULL DEFAULT '0',
  `title` varchar(104) NOT NULL DEFAULT '',
  PRIMARY KEY (`family_id`,`character_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Family members';


CREATE TABLE IF NOT EXISTS `friends` (
  `id` int NOT NULL,
  `friend_id` int NOT NULL,
  `owner` int NOT NULL,
  `status` tinyint unsigned NOT NULL DEFAULT '0',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`,`owner`) USING BTREE,
  UNIQUE KEY `uk_friends_owner_friend` (`owner`,`friend_id`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Friendslist';


CREATE TABLE IF NOT EXISTS `housings` (
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
  `sell_public` tinyint unsigned NOT NULL DEFAULT '1',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Player buildings';

-- ----------------------------
-- Records of housings
-- ----------------------------
INSERT IGNORE INTO `housings` VALUES (1, 0, 0, 0, 139, 'Archeum Lodestone', 19643., 24385.4, 168.9, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', '2043-03-03 00:00:00', 2, 0, 0, 0);
INSERT IGNORE INTO `housings` VALUES (2, 0, 0, 0, 184, 'Archeum Lodestone', 19952.6, 24275.5, 140.4, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', '2043-03-03 00:00:00', 2, 0, 0, 0);
INSERT IGNORE INTO `housings` VALUES (3, 0, 0, 0, 185, 'Archeum Lodestone', 20379.4, 24126.2, 123.6, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', '2043-03-03 00:00:00', 2, 0, 0, 0);
INSERT IGNORE INTO `housings` VALUES (4, 0, 0, 0, 186, 'Archeum Lodestone', 21235.7, 23918.5, 165.0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', '2043-03-03 00:00:00', 2, 0, 0, 0);
INSERT IGNORE INTO `housings` VALUES (5, 0, 0, 0, 187, 'Archeum Lodestone', 21449.961, 24210.300, 154.376, 0, 0, -0.205, 0, 0, 0, '0001-01-01 00:00:00', '2043-03-03 00:00:00', 2, 0, 0, 0);
INSERT IGNORE INTO `housings` VALUES (6, 0, 0, 0, 188, 'Archeum Lodestone', 22048.2, 24241.1, 154.8, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', '2043-03-03 00:00:00', 2, 0, 0, 0);
INSERT IGNORE INTO `housings` VALUES (7, 0, 0, 0, 189, 'Archeum Lodestone', 19644.0, 25077.6, 164.6, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', '2043-03-03 00:00:00', 2, 0, 0, 0);
INSERT IGNORE INTO `housings` VALUES (8, 0, 0, 0, 190, 'Archeum Lodestone', 20325.6, 25174.6, 172.9, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', '2043-03-03 00:00:00', 2, 0, 0, 0);
INSERT IGNORE INTO `housings` VALUES (9, 0, 0, 0, 191, 'Archeum Lodestone', 20890.8, 25238.5, 193.7, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', '2043-03-03 00:00:00', 2, 0, 0, 0);
INSERT IGNORE INTO `housings` VALUES (10, 0, 0, 0, 192, 'Archeum Lodestone', 21956, 24881.7, 206.3, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', '2043-03-03 00:00:00', 2, 0, 0, 0);
INSERT IGNORE INTO `housings` VALUES (11, 0, 0, 0, 271, 'Archeum Lodestone', 23060.8, 25148.3, 142.0, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', '2043-03-03 00:00:00', 2, 0, 0, 0);
INSERT IGNORE INTO `housings` VALUES (12, 0, 0, 0, 272, 'Archeum Lodestone', 21800.3, 26893.9, 137.7, 0, 0, 0, 0, 0, 0, '0001-01-01 00:00:00', '2043-03-03 00:00:00', 2, 0, 0, 0);

CREATE TABLE IF NOT EXISTS `items` (
  `id` bigint unsigned NOT NULL,
  `type` varchar(100) NOT NULL,
  `template_id` int unsigned NOT NULL,
  `container_id` int unsigned NOT NULL DEFAULT '0',
  `slot_type` int NOT NULL DEFAULT 0 COMMENT 'Internal Container Type',
  `slot` int NOT NULL,
  `count` int NOT NULL,
  `detail_type` tinyint unsigned NOT NULL DEFAULT '0',
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


CREATE TABLE IF NOT EXISTS `mails` (
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
  `sender_deleted` tinyint(1) NOT NULL DEFAULT '0',
  `receiver_deleted` tinyint(1) NOT NULL DEFAULT '0',
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


CREATE TABLE IF NOT EXISTS `mates` (
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


CREATE TABLE IF NOT EXISTS `options` (
  `key` varchar(100) NOT NULL,
  `value` text CHARACTER SET utf8mb4 NOT NULL,
  `owner` int unsigned NOT NULL,
  PRIMARY KEY (`key`,`owner`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Settings that the client stores on the server';


CREATE TABLE IF NOT EXISTS `portal_book_coords` (
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


CREATE TABLE IF NOT EXISTS `portal_visited_district` (
  `id` int NOT NULL,
  `subzone` int NOT NULL,
  `owner` int NOT NULL,
  PRIMARY KEY (`id`,`subzone`,`owner`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='List of visited area for the portal book';


CREATE TABLE IF NOT EXISTS `quests` (
  `id` int unsigned NOT NULL,
  `template_id` int unsigned NOT NULL,
  `data` tinyblob NOT NULL,
  `status` tinyint NOT NULL,
  `owner` int unsigned NOT NULL,
  PRIMARY KEY (`id`,`owner`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Currently open quests';


CREATE TABLE IF NOT EXISTS `skills` (
  `id` int unsigned NOT NULL,
  `level` tinyint NOT NULL,
  `type` enum('Skill','Buff') NOT NULL,
  `owner` int unsigned NOT NULL,
  PRIMARY KEY (`id`,`owner`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Learned character skills';


CREATE TABLE IF NOT EXISTS `specialty_market_revision` (
  `id` tinyint unsigned NOT NULL,
  `revision` bigint NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  CHECK (`id` = 1),
  CHECK (`revision` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

INSERT INTO `specialty_market_revision` (`id`, `revision`) VALUES (1, 0)
ON DUPLICATE KEY UPDATE `id` = `id`;

CREATE TABLE IF NOT EXISTS `specialty_market_routes` (
  `item_id` int unsigned NOT NULL,
  `zone_group_id` int unsigned NOT NULL,
  `ratio` int NOT NULL,
  `demand_remainder` int NOT NULL DEFAULT 0,
  PRIMARY KEY (`item_id`, `zone_group_id`),
  CHECK (`item_id` > 0 AND `zone_group_id` > 0),
  CHECK (`ratio` >= 0 AND `demand_remainder` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

CREATE TABLE IF NOT EXISTS `specialty_market_contributions` (
  `zone_group_id` int unsigned NOT NULL,
  `tag_id` int unsigned NOT NULL,
  `sequence` bigint unsigned NOT NULL,
  `item_id` int unsigned NOT NULL,
  `amount` int unsigned NOT NULL,
  PRIMARY KEY (`zone_group_id`, `tag_id`, `sequence`),
  CHECK (`zone_group_id` > 0 AND `tag_id` > 0),
  CHECK (`sequence` > 0 AND `item_id` > 0 AND `amount` > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

CREATE TABLE IF NOT EXISTS `specialty_market_cargo` (
  `zone_group_id` int unsigned NOT NULL,
  `trade_good_id` int unsigned NOT NULL,
  `amount` int unsigned NOT NULL,
  PRIMARY KEY (`zone_group_id`, `trade_good_id`),
  CHECK (`zone_group_id` > 0 AND `trade_good_id` > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

CREATE TABLE IF NOT EXISTS `specialty_market_history` (
  `item_id` int unsigned NOT NULL,
  `zone_group_id` int unsigned NOT NULL,
  `sequence` tinyint unsigned NOT NULL,
  `ratio` int NOT NULL,
  `recorded` bigint NOT NULL,
  PRIMARY KEY (`item_id`, `zone_group_id`, `sequence`),
  CHECK (`item_id` > 0 AND `zone_group_id` > 0),
  CHECK (`ratio` >= 0 AND `recorded` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

CREATE TABLE IF NOT EXISTS `specialty_market_stock_event_checks` (
  `trigger_id` int unsigned NOT NULL,
  `next_check` bigint NOT NULL,
  PRIMARY KEY (`trigger_id`),
  CHECK (`trigger_id` > 0 AND `next_check` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

CREATE TABLE IF NOT EXISTS `specialty_market_stock_events` (
  `event_id` int unsigned NOT NULL,
  `started_at` bigint NOT NULL,
  `expires_at` bigint NOT NULL,
  PRIMARY KEY (`event_id`),
  CHECK (`event_id` > 0),
  CHECK (`started_at` >= 0 AND `expires_at` > `started_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;


CREATE TABLE IF NOT EXISTS `uccs` (
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


CREATE TABLE IF NOT EXISTS `music` (
  `id` int NOT NULL AUTO_INCREMENT,
  `author` int NOT NULL COMMENT 'PlayerId',
  `title` varchar(128) NOT NULL,
  `song` text NOT NULL COMMENT 'Song MML',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='User Created Content (music)';


CREATE TABLE IF NOT EXISTS `item_containers` (
  `container_id` int unsigned NOT NULL,
  `container_type` varchar(64) COLLATE 'utf8mb4_general_ci' NOT NULL DEFAULT 'ItemContainer' COMMENT 'Partial Container Class Name',
  `slot_type` int NOT NULL DEFAULT 0 COMMENT 'Internal Container Type',
  `container_size` int NOT NULL DEFAULT '50' COMMENT 'Maximum Container Size',
  `owner_id` int unsigned NOT NULL COMMENT 'Owning Character Id',
  `mate_id` int unsigned NOT NULL DEFAULT '0' COMMENT 'Owning Mate Id',
  `parent_item_id` bigint unsigned NOT NULL DEFAULT '0' COMMENT 'Owning ItemBag instance Id',
  PRIMARY KEY (`container_id`),
  KEY `idx_item_containers_parent_item_id` (`parent_item_id`)
) COLLATE 'utf8mb4_general_ci';


CREATE TABLE IF NOT EXISTS `slaves` (
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


CREATE TABLE IF NOT EXISTS `ics_skus` (
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


CREATE TABLE IF NOT EXISTS `ics_shop_items` (
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


CREATE TABLE IF NOT EXISTS `ics_menu` (
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


CREATE TABLE IF NOT EXISTS `audit_ics_sales` (
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

CREATE TABLE IF NOT EXISTS `expedition_recruitments` (
  `expedition_id` INT NOT NULL,
  `interest_mask` SMALLINT NOT NULL,
  `introduction` VARCHAR(100) NOT NULL,
  `registered_at` DATETIME(6) NOT NULL,
  `expires_at` DATETIME(6) NOT NULL,
  PRIMARY KEY (`expedition_id`),
  KEY `idx_expedition_recruitments_expiry` (`expires_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `expedition_recruitment_applications` (
  `expedition_id` INT NOT NULL,
  `character_id` INT UNSIGNED NOT NULL,
  `memo` VARCHAR(100) NOT NULL,
  `registered_at` DATETIME(6) NOT NULL,
  PRIMARY KEY (`expedition_id`, `character_id`),
  KEY `idx_expedition_recruitment_applications_character` (`character_id`, `registered_at`),
  CONSTRAINT `fk_expedition_recruitment_applications_recruitment` FOREIGN KEY (`expedition_id`) REFERENCES `expedition_recruitments` (`expedition_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;


CREATE TABLE IF NOT EXISTS `audit_char_sus` (
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

CREATE TABLE IF NOT EXISTS `crime` (
	`id` INT UNSIGNED NOT NULL DEFAULT '0' COMMENT 'Crime point Id',
	`criminal` INT UNSIGNED NULL DEFAULT NULL COMMENT 'Player Id of the criminal',
	`victim` INT UNSIGNED NULL DEFAULT NULL COMMENT 'Player Id of the victim',
	`reporter` INT UNSIGNED NULL DEFAULT NULL COMMENT 'Player Id of the reporter',
	`crime_type` INT UNSIGNED NULL DEFAULT NULL COMMENT 'Crime Type Id',
	`doodad_template` INT UNSIGNED NULL DEFAULT NULL COMMENT 'Related doodad template',
	`zone_key` INT UNSIGNED NULL DEFAULT NULL COMMENT 'Zone group Id of where the crime happened',
	`x` FLOAT NULL DEFAULT '0',
	`y` FLOAT NULL DEFAULT '0',
	`z` FLOAT NULL DEFAULT '0',
	`crime_time` DATETIME NULL DEFAULT NULL,
	`report_time` DATETIME NULL DEFAULT NULL,
	`arg1` INT UNSIGNED NULL DEFAULT NULL COMMENT 'Argument1 of reported crime',
	`arg2` INT UNSIGNED NULL DEFAULT NULL COMMENT 'Argument2 of reported crime',
	`arg3` INT UNSIGNED NULL DEFAULT NULL COMMENT 'Argument3 of reported crime',
	`msg` TEXT NULL DEFAULT NULL COLLATE 'utf8mb4_general_ci',
	PRIMARY KEY (`id`) USING BTREE
)
COMMENT='Keeps track of the crime events'
COLLATE='utf8mb4_general_ci'
ENGINE=InnoDB
;
