CREATE TABLE `expedition_recruitments` (
  `expedition_id` INT NOT NULL,
  `interest_mask` SMALLINT NOT NULL,
  `introduction` VARCHAR(100) NOT NULL,
  `registered_at` DATETIME(6) NOT NULL,
  `expires_at` DATETIME(6) NOT NULL,
  PRIMARY KEY (`expedition_id`),
  KEY `idx_expedition_recruitments_expiry` (`expires_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE `expedition_recruitment_applications` (
  `expedition_id` INT NOT NULL,
  `character_id` INT UNSIGNED NOT NULL,
  `memo` VARCHAR(100) NOT NULL,
  `registered_at` DATETIME(6) NOT NULL,
  PRIMARY KEY (`expedition_id`, `character_id`),
  KEY `idx_expedition_recruitment_applications_character` (`character_id`, `registered_at`),
  CONSTRAINT `fk_expedition_recruitment_applications_recruitment` FOREIGN KEY (`expedition_id`) REFERENCES `expedition_recruitments` (`expedition_id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
