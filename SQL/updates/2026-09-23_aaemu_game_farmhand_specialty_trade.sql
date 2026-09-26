USE aaemu_game;

CREATE TABLE IF NOT EXISTS `character_butler_specialty_trade_jobs` (
  `id` bigint NOT NULL AUTO_INCREMENT,
  `character_id` int unsigned NOT NULL,
  `npc_id` int unsigned NOT NULL,
  `specialty_type` int unsigned NOT NULL,
  `to_zone_group_type` smallint unsigned NOT NULL,
  `product_item_id` int unsigned NOT NULL,
  `created_time` bigint NOT NULL,
  `delivery_time` int unsigned NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_character_butler_specialty_trade_job` (`character_id`, `specialty_type`, `to_zone_group_type`),
  KEY `idx_character_butler_specialty_trade_jobs_character` (`character_id`),
  CONSTRAINT `fk_character_butler_specialty_trade_jobs_butler`
    FOREIGN KEY (`character_id`) REFERENCES `character_butlers` (`character_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Durable farmhand specialty-trade jobs';
