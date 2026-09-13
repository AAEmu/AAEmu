USE aaemu_game;

ALTER TABLE `character_butlers`
  ADD COLUMN `lp_charge_reset_time` bigint NOT NULL DEFAULT 0 AFTER `lp_charged_amount`;

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
