USE aaemu_game;

-- One persistent farmhand per character. A NULL house_id represents an unbound farmhand while
-- retaining its name and accumulated resources; the unique key prevents cross-character binding.
CREATE TABLE IF NOT EXISTS `character_butlers` (
  `character_id` int unsigned NOT NULL,
  `house_id` int unsigned DEFAULT NULL,
  `name` varchar(128) NOT NULL DEFAULT '',
  `labor_power` int unsigned NOT NULL DEFAULT 0,
  `lp_charged_amount` smallint unsigned NOT NULL DEFAULT 0,
  `remain_production_cost` smallint unsigned NOT NULL DEFAULT 0,
  PRIMARY KEY (`character_id`),
  UNIQUE KEY `ux_character_butlers_house` (`house_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Character-owned farmhand state and durable house binding';
